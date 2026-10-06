using AgentHub.Agents.Healthy;
using AgentHub.Agents.Healthy.Abstractions;
using AgentHub.Data;
using AgentHub.WebApi.Blob;
using AgentHub.WebApi.Healthy;
using AgentHub.WebApi.Heartbeat;
using AgentHub.WebApi.Llm;
using AgentHub.WebApi.Options;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "AgentHub.WebApi")
        .WriteTo.Console());

    builder.Services.Configure<ServiceBusOptions>(
        builder.Configuration.GetSection(ServiceBusOptions.SectionName));
    builder.Services.Configure<HealthyWorkerOptions>(
        builder.Configuration.GetSection(HealthyWorkerOptions.SectionName));
    builder.Services.Configure<BlobStorageOptions>(
        builder.Configuration.GetSection(BlobStorageOptions.SectionName));

    builder.Services.AddAgentHubData(builder.Configuration);
    builder.Services.AddAgentHubLlm(builder.Configuration);

    builder.Services.AddSingleton<IHeartbeatPublisher, ServiceBusHeartbeatPublisher>();
    builder.Services.AddHostedService<HeartbeatPingConsumer>();

    builder.Services.AddSingleton<IArtifactBlobStore, AzureArtifactBlobStore>();
    builder.Services.AddSingleton<IHealthyTaskPublisher, ServiceBusHealthyTaskPublisher>();
    builder.Services.AddSingleton<IAgentResultPublisher, ServiceBusAgentResultPublisher>();
    builder.Services.AddSingleton<HealthyAgentHandler>(sp =>
    {
        var pauseMs = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthyWorkerOptions>>()
            .Value.DeliberatePauseMs;
        return new HealthyAgentHandler(
            sp.GetRequiredService<ILlmClient>(),
            sp.GetRequiredService<IArtifactBlobStore>(),
            sp.GetRequiredService<AgentHub.Data.Repositories.IEffectLedgerRepository>(),
            sp.GetRequiredService<AgentHub.Data.Repositories.IActivityLogRepository>(),
            sp.GetRequiredService<ILogger<HealthyAgentHandler>>(),
            TimeSpan.FromMilliseconds(Math.Max(0, pauseMs)));
    });
    builder.Services.AddSingleton<HealthyAgentDispatcher>();
    builder.Services.AddHostedService<HealthyAgentWorker>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "AgentHub API",
            Version = "v1",
            Description = "AgentHub agent host Web API (HEALTHY dummy agent + heartbeat)."
        });
    });

    builder.Services.AddOpenApi();

    var serviceBusConnection = builder.Configuration.GetSection(ServiceBusOptions.SectionName)["ConnectionString"];
    var healthChecks = builder.Services.AddHealthChecks();
    if (!string.IsNullOrWhiteSpace(serviceBusConnection))
    {
        healthChecks.AddAzureServiceBusTopic(
            serviceBusConnection,
            topicName: builder.Configuration.GetSection(ServiceBusOptions.SectionName)["PingTopic"]
                       ?? "ah-heartbeat-ping",
            name: "servicebus-ping-topic",
            tags: ["ready"]);
    }

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AgentHub API v1");
        });
    }

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHeartbeatEndpoints();
    app.MapHealthyEndpoints();

    app.MapGet("/", () => Results.Ok(new
    {
        service = "AgentHub.WebApi",
        status = "ok",
        docs = "/swagger",
        healthy = "/api/connectivity/healthy"
    }));

    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "AgentHub.WebApi terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

public partial class Program;
