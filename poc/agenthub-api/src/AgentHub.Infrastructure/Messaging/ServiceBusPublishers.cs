using System.Text.Json;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Infrastructure.Messaging;

/// <summary>
/// Worker options for the HEALTHY harness subscription (Workers:Healthy).
/// Defaults match D-I2-2; production agents get their own options sections later.
/// </summary>
public sealed class HealthyWorkerOptions
{
    public const string SectionName = "Workers:Healthy";

    public bool Enabled { get; set; } = true;

    public string Topic { get; set; } = "sdlc-tasks-healthy";

    public string Subscription { get; set; } = "healthy-tasks";

    public string ResultsTopic { get; set; } = "sdlc-results";

    public int MaxDeliveryCount { get; set; } = 5;

    /// <summary>ISO 8601 duration, e.g. 00:02:00.</summary>
    public string MaxAutoLockRenewalDuration { get; set; } = "00:02:00";

    /// <summary>Deliberate pause inside the handler (milliseconds).</summary>
    public int DeliberatePauseMs { get; set; } = 250;

    public int MaxConcurrentCalls { get; set; } = 1;
}

/// <summary>Worker options for the POC pipeline walker (Workers:Pipeline).</summary>
public sealed class PipelineWorkerOptions
{
    public const string SectionName = "Workers:Pipeline";

    public bool Enabled { get; set; } = true;

    public string Topic { get; set; } = "agenthub.pipeline.agent";

    public string Subscription { get; set; } = "agenthub-pipeline";

    public string ResultsTopic { get; set; } = "buildgenix.agent-results";

    public string EventsTopic { get; set; } = "buildgenix.agent-events";

    public int MaxDeliveryCount { get; set; } = 5;

    public string MaxAutoLockRenewalDuration { get; set; } = "00:30:00";

    public int MaxConcurrentCalls { get; set; } = 1;

    /// <summary>When true, skip Service Bus processor and rely on in-process dispatch.</summary>
    public bool InProcessOnly { get; set; } = true;

    /// <summary>
    /// When true, run PRD/SD/IA/TA/Deploy as separate topic BackgroundServices (preferred).
    /// Legacy monolithic PipelineAgentWorker stays idle when this is true.
    /// </summary>
    public bool UseTopicAgents { get; set; } = true;
}

public sealed class ServiceBusHealthyTaskPublisher : IHealthyTaskPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly HealthyWorkerOptions _worker;

    public ServiceBusHealthyTaskPublisher(
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<HealthyWorkerOptions> worker)
    {
        _worker = worker.Value;
        var cs = serviceBus.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("ServiceBus:ConnectionString is required.");
        }

        _client = new ServiceBusClient(cs);
        _sender = _client.CreateSender(_worker.Topic);
    }

    public async Task PublishAsync(TaskMessage message, CancellationToken cancellationToken = default)
    {
        message.EnsureDefaults();
        var errors = message.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var body = JsonSerializer.Serialize(message, JsonOptions);
        var sbMessage = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = message.LogicalMessageId,
            CorrelationId = message.CorrelationId,
            Subject = message.Stage
        };

        await _sender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class ServiceBusAgentResultPublisher : IAgentResultPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly HealthyWorkerOptions _worker;
    private readonly ILogger<ServiceBusAgentResultPublisher> _logger;

    public ServiceBusAgentResultPublisher(
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<HealthyWorkerOptions> worker,
        ILogger<ServiceBusAgentResultPublisher> logger)
    {
        _worker = worker.Value;
        _logger = logger;
        var cs = serviceBus.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("ServiceBus:ConnectionString is required.");
        }

        _client = new ServiceBusClient(cs);
        _sender = _client.CreateSender(_worker.ResultsTopic);
    }

    public async Task PublishResultAsync(AgentResult result, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(result, JsonOptions);
        var sbMessage = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = $"{result.RunId}:result",
            CorrelationId = result.CorrelationId,
            Subject = $"AgentResult:{result.Status}"
        };

        await _sender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Published AgentResult Status={Status} to {Topic} for TaskId={TaskId}",
            result.Status,
            _worker.ResultsTopic,
            result.TaskId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
