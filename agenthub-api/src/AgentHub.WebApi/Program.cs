using System.Text.Json;
using AgentHub.Data;
using AgentHub.Hosting;
using AgentHub.Infrastructure;
using AgentHub.Infrastructure.Options;
using AgentHub.WebApi.Health;
using AgentHub.WebApi.Healthy;
using AgentHub.WebApi.Heartbeat;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

    builder.Services.AddAgentHubData(builder.Configuration);
    builder.Services.AddAgentHubInfrastructure(builder.Configuration);
    builder.Services.AddAgentWorkers(builder.Configuration);

    builder.Services.AddSingleton<IHeartbeatPublisher, ServiceBusHeartbeatPublisher>();
    builder.Services.AddHostedService<HeartbeatPingConsumer>();

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
    builder.Services.AddHttpClient(nameof(ServiceBusEmulatorHealthCheck));

    var serviceBusSection = builder.Configuration.GetSection(ServiceBusOptions.SectionName);
    var serviceBusConnection = serviceBusSection["ConnectionString"];
    var pingTopic = serviceBusSection["PingTopic"] ?? "ah-heartbeat-ping";
    var usesEmulator = !string.IsNullOrWhiteSpace(serviceBusConnection)
                       && serviceBusConnection.Contains(
                           "UseDevelopmentEmulator=true",
                           StringComparison.OrdinalIgnoreCase);
    var healthChecks = builder.Services.AddHealthChecks();
    if (!string.IsNullOrWhiteSpace(serviceBusConnection))
    {
        if (usesEmulator)
        {
            healthChecks.AddCheck<ServiceBusEmulatorHealthCheck>(
                "servicebus-emulator",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);
        }
        else
        {
            healthChecks.AddAzureServiceBusTopic(
                serviceBusConnection,
                topicName: pingTopic,
                name: "servicebus-ping-topic",
                tags: ["ready"]);
        }
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

    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = WriteHealthJsonAsync
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
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

static Task WriteHealthJsonAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        entries = report.Entries.ToDictionary(
            e => e.Key,
            e => new
            {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message
            })
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

public partial class Program;
