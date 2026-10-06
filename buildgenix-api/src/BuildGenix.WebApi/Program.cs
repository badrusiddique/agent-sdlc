using BuildGenix.Data;
using BuildGenix.WebApi.Auth;
using BuildGenix.WebApi.Heartbeat;
using BuildGenix.WebApi.Options;
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

    builder.Services.AddSingleton<IHeartbeatPublisher, ServiceBusHeartbeatPublisher>();
    builder.Services.AddHostedService<HeartbeatPingConsumer>();

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

    var serviceBusConnection = builder.Configuration.GetSection(ServiceBusOptions.SectionName)["ConnectionString"];
    var healthChecks = builder.Services.AddHealthChecks();
    if (!string.IsNullOrWhiteSpace(serviceBusConnection))
    {
        healthChecks.AddAzureServiceBusTopic(
            serviceBusConnection,
            topicName: builder.Configuration.GetSection(ServiceBusOptions.SectionName)["PingTopic"]
                       ?? "bg-heartbeat-ping",
            name: "servicebus-ping-topic",
            tags: ["ready"]);
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

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHeartbeatEndpoints();
    app.MapControllers();

    app.MapGet("/", () => Results.Ok(new
    {
        service = "BuildGenix.WebApi",
        status = "ok",
        docs = "/swagger"
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

public partial class Program;
