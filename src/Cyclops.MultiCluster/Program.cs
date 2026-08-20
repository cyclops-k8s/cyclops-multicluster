using Destructurama;
using k8s.Models;
using KubeOps.Abstractions.Builder;
using KubeOps.KubernetesClient;
using KubeOps.Operator;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Cyclops.MultiCluster.Controllers;
using Cyclops.MultiCluster.Models.K8sEntities;
using Cyclops.MultiCluster.Services;
using Cyclops.MultiCluster.Services.Authentication;
using Cyclops.MultiCluster.Services.Default;

const string OperatorFlag = "--operator";
const string OrchestratorFlag = "--orchestrator";
const string DnsServerFlag = "--dns-server";
const string FrontEndFlag = "--front-end";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.logging.json")
                      .AddEnvironmentVariables()
                      .AddCommandLine(args);

builder.Services.Configure<ApiAuthenticationHandlerOptions>(builder.Configuration.GetSection("Authentication"));

builder.Services.Configure<MultiClusterOptions>(builder.Configuration);
var options = new MultiClusterOptions();
builder.Configuration.Bind(options);

//Enable http/2 only
// .net core only allows one http protocol on http ports. GRPC requires http/2. So we force it.
builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(options.ListenPort, (lo) =>
    {
        lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });

    if (args.Contains(DnsServerFlag))
    {
        o.ListenAnyIP(options.ListenGrpcPort, (lo) =>
        {
            lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
        });
    }
});

builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration)
                 .Destructure.UsingAttributes();
});

if (args.Contains(OperatorFlag))
{
    builder.Services.AddSingleton<OperatorLeader>();
    builder.Services.AddKubernetesOperator((operatorSettings) => operatorSettings
            .WithAutoAttachFinalizers(false)
            .WithAutoDetachFinalizers(false)
            .WithName("operator")
            .WithLeaderElection(LeaderElectionType.Single))
        .AddController<K8sChangedController, V1Ingress>()
        .AddController<K8sChangedController, V1Service>()
        .AddController<K8sChangedController, V1EndpointSlice>()
        .AddController<K8sChangedController, V1Gslb>()
        .AddController<K8sChangedController, V1VeccGslb>();
}
else if (args.Contains(OrchestratorFlag))
{
    builder.Services.AddSingleton<OrchestratorLeader>();
    builder.Services.AddKubernetesOperator((operatorSettings) => operatorSettings
            .WithAutoAttachFinalizers(false)
            .WithAutoDetachFinalizers(false)
            .WithName("orchestrator")
            .WithNamespace(options.Namespace)
            .WithLeaderElection(LeaderElectionType.Single))
        .AddController<K8sClusterCacheController, V1ClusterCache>();
}
else if (args.Contains(DnsServerFlag))
{
    builder.Services.AddKubernetesOperator((operatorSettings) => operatorSettings
            .WithAutoAttachFinalizers(false)
            .WithAutoDetachFinalizers(false)
            .WithName("dnsserver")
            .WithNamespace(options.Namespace)
            .WithLeaderElection(LeaderElectionType.None))
        .AddController<K8sHostnameCacheController, V1HostnameCache>();

    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
    builder.Services.AddGrpc((o) =>
    {
        o.EnableDetailedErrors = true;
    });
}
else if (args.Contains(FrontEndFlag))
{
    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
}
else
{
    throw new Exception($"Expected one of {OperatorFlag}, {OrchestratorFlag}, {DnsServerFlag} or {FrontEndFlag}");
}

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBasicCache, BasicCache>();
builder.Services.AddSingleton<IKubernetesCache, KubernetesApiCache>();
builder.Services.AddSingleton<LeaderStatus>();
builder.Services.AddSingleton<DefaultDnsResolver>();
builder.Services.AddSingleton<IGslbManager, DefaultGslbManager>();
builder.Services.AddSingleton<IIngressManager, DefaultIngressManager>();
builder.Services.AddSingleton<IServiceManager, DefaultServiceManager>();
builder.Services.AddSingleton<IHostnameSynchronizer, DefaultHostnameSynchronizer>();
builder.Services.AddSingleton<ICache, MemoryCache>();
builder.Services.AddSingleton<IDateTimeProvider, DefaultDateTimeProvider>();
builder.Services.AddSingleton<IRandom, DefaultRandom>();
builder.Services.AddSingleton<DefaultDnsResolver>();
builder.Services.AddSingleton<KubernetesQueue>();
builder.Services.AddSingleton<IQueue>((s) => s.GetRequiredService<KubernetesQueue>());
builder.Services.AddScoped<ApiAuthenticationHandler>();
builder.Services.AddSingleton<ApiAuthenticationHasher>();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication(ApiAuthenticationHandlerOptions.DefaultScheme)
    .AddScheme<ApiAuthenticationHandlerOptions, ApiAuthenticationHandler>(ApiAuthenticationHandlerOptions.DefaultScheme, null);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("X-Api-Key", new OpenApiSecurityScheme{
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey
    });
    options.IncludeXmlComments(typeof(Program).Assembly);
    options.OperationFilter<SwaggerOperationFilter>();
});


foreach (var peer in options.Peers)
{
    builder.Services.AddHttpClient(peer.Url, client =>
    {
        client.BaseAddress = new Uri(peer.Url);
        client.DefaultRequestHeaders.Add("X-Api-Key", peer.Key);
    });
}

var app = builder.Build();

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/Healthz"), appBuilder => appBuilder.UseSerilogRequestLogging());
app.UseSwagger();
app.UseSwagger(options =>
{
    options.RouteTemplate = "/openapi/{documentName}.json";
});
app.MapScalarApiReference();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting");
logger.LogInformation("Configured Options {@options}", options);

var processTasks = new List<Task>();

// watches the cluster caches and updates the host cache, also expires old cluster caches
if (args.Contains(OperatorFlag))
{
    logger.LogInformation("Running the operator");

    var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();

    processTasks.Add(Task.Run(async () =>
    {
        logger.LogInformation("Starting the operator leader watcher");
        var leaderStateChanged = app.Services.GetRequiredService<OperatorLeader>();
        var lifecycle = app.Lifetime;

        while (!lifecycle.ApplicationStopping.IsCancellationRequested)
        {
            await Task.Yield();
            await Task.Delay(1000);
        };
    }).ContinueWith(_ => logger.LogInformation("Operator leader watcher stopped")));

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting cluster heartbeat");
        return hostnameSynchronizer.ClusterHeartbeatAsync().ContinueWith(_ =>
        {
            logger.LogInformation("Cluster heartbeat stopped");
        });
    }));

    if (options.PeriodicRefreshInterval <= 0)
    {
        logger.LogInformation("Perioid refresh interval is {interval} which is <= 0, disabling periodic refresher.", options.PeriodicRefreshInterval);
    }
    else
    {
        processTasks.Add(Task.Run(async () =>
        {
            logger.LogInformation("Starting the periodic refresher");
            var lifecycle = app.Lifetime;
            var leaderStatus = app.Services.GetRequiredService<LeaderStatus>();

            while (!lifecycle.ApplicationStopping.IsCancellationRequested)
            {
                await Task.Yield();
                await Task.Delay(options.PeriodicRefreshInterval * 1000);
                using var scope = logger.BeginScope(new { PeriodicRefreshId = Guid.NewGuid() });

                if (leaderStatus.IsLeader)
                {
                    logger.LogInformation("Initiating periodic refresh");
                    try
                    {
                        await hostnameSynchronizer.SynchronizeLocalClusterAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error during periodic refresh");
                    }
                }
                else
                {
                    logger.LogTrace("Not the leader, skipping periodic refresh");
                }
            };
        }).ContinueWith(_ => logger.LogInformation("Periodic refresher stopped")));
    }

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server for health checks");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

//watches cluster events and keeps the local cluster config in sync and sends updates to other nodes
else if (args.Contains(OrchestratorFlag))
{
    logger.LogInformation("Running the orchestrator");

    processTasks.Add(Task.Run(() =>
    {
        var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();
        logger.LogInformation("Starting cluster heartbeat watcher");
        return hostnameSynchronizer.WatchClusterHeartbeatsAsync().ContinueWith(_ => logger.LogInformation("Cluster heartbeat watcher stopped"));
    }));

    processTasks.Add(Task.Run(async () =>
    {
        logger.LogInformation("Starting the orchestrator leader watcher");
        var leaderStateChanged = app.Services.GetRequiredService<OrchestratorLeader>();
        while (true)
        {
            await Task.Yield();
            await Task.Delay(1000);
        }
    }).ContinueWith(_ => logger.LogInformation("Orchestrator leader watcher stopped")));


    if (options.PeriodicRefreshInterval <= 0)
    {
        logger.LogInformation("Perioid refresh interval is {interval} which is <= 0, disabling periodic refresher.", options.PeriodicRefreshInterval);
    }
    else
    {
        processTasks.Add(Task.Run(async () =>
        {
            logger.LogInformation("Starting the periodic refresher");
            var lifecycle = app.Lifetime;
            var leaderStatus = app.Services.GetRequiredService<LeaderStatus>();
            var cache = app.Services.GetRequiredService<ICache>();

            while (!lifecycle.ApplicationStopping.IsCancellationRequested)
            {
                await Task.Yield();
                await Task.Delay(options.PeriodicRefreshInterval * 1000);
                using var scope = logger.BeginScope(new { PeriodicRefreshId = Guid.NewGuid() });

                if (leaderStatus.IsLeader)
                {
                    logger.LogInformation("Initiating periodic refresh");
                    try
                    {
                        await cache.SynchronizeCachesAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error during periodic refresh");
                    }
                }
                else
                {
                    logger.LogTrace("Not the leader, skipping periodic refresh");
                }
            };
        }).ContinueWith(_ => logger.LogInformation("Periodic refresher stopped")));
    }

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server for health checks");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

//starts the dns server to respond to dns queries for the respective hosts
else if (args.Contains(DnsServerFlag))
{
    logger.LogInformation("Running the dns server");

    var dnsResolver = app.Services.GetRequiredService<DefaultDnsResolver>();
    var queue = app.Services.GetRequiredService<IQueue>();

    await dnsResolver.InitializeAsync();
    queue.OnHostChangedAsync = dnsResolver.OnHostChangedAsync;

    app.MapGrpcService<DefaultDnsService>()
        .WithHttpLogging(Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All)
        .AllowAnonymous();

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server for health checks");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

//starts the api server
else if (args.Contains(FrontEndFlag))
{
    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

logger.LogInformation("Waiting on process tasks");

await Task.WhenAny(processTasks);

logger.LogInformation("Terminated");
