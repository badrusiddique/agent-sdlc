using System.Text.Json;
using BuildGenix.Data;
using BuildGenix.WebApi.Auth;
using BuildGenix.WebApi.Health;
using BuildGenix.WebApi.Heartbeat;
using BuildGenix.WebApi.Options;
using BuildGenix.WebApi.Poc;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: safe for WebApplicationFactory / parallel test hosts
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BuildGenix.WebApi")
        .WriteTo.Console(),
        preserveStaticLogger: true);

    builder.Services.Configure<ServiceBusOptions>(
        builder.Configuration.GetSection(ServiceBusOptions.SectionName));

    builder.Services.AddBuildGenixData(builder.Configuration);

    builder.Services.Configure<PocPipelineOptions>(
        builder.Configuration.GetSection(PocPipelineOptions.SectionName));
    builder.Services.AddSingleton<PocPipelineOrchestrator>();
    builder.Services.AddHttpClient("agenthub", (sp, client) =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PocPipelineOptions>>().Value;
        client.BaseAddress = new Uri(opts.AgentHubBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromMinutes(10);
    });

    var enableHeartbeat = builder.Configuration.GetValue("ServiceBus:EnableConsumer", false);
    if (enableHeartbeat)
    {
        builder.Services.AddSingleton<IHeartbeatPublisher, ServiceBusHeartbeatPublisher>();
        builder.Services.AddHostedService<HeartbeatPingConsumer>();
    }
    else
    {
        builder.Services.AddSingleton<IHeartbeatPublisher, NoOpHeartbeatPublisher>();
    }

    builder.Services.AddControllers();
    builder.Services.AddProblemDetails();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "BuildGenix API",
            Version = "v1",
            Description =
                "BuildGenix orchestration Web API. Controllers follow docs/buildgenix-api-contracts.md (D-C6). " +
                "Phase 0: dummy X-User-Id auth (D-CL-9). Create WI does not take ~ACTIVE; Implement now does (D-C4b)."
        });
    });

    builder.Services.AddOpenApi();
    builder.Services.AddHttpClient(nameof(ServiceBusEmulatorHealthCheck));

    var serviceBusSection = builder.Configuration.GetSection(ServiceBusOptions.SectionName);
    var serviceBusConnection = serviceBusSection["ConnectionString"];
    var pingTopic = serviceBusSection["PingTopic"] ?? "bg-heartbeat-ping";
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
    app.UseDummyUserId();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "BuildGenix API v1");
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
    app.MapControllers();

    app.MapGet("/", () => Results.Ok(new
    {
        service = "BuildGenix.WebApi",
        status = "ok",
        docs = "/swagger",
        pocPipeline = "/api/poc/pipeline/run"
    }));

    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "BuildGenix.WebApi terminated unexpectedly");
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
