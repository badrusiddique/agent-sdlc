using System.Text.Json;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Infrastructure.Messaging;

public interface IPipelineTaskPublisher
{
    Task PublishAsync(TaskMessage message, CancellationToken cancellationToken = default);
}

public sealed class NoOpPipelineTaskPublisher : IPipelineTaskPublisher
{
    public Task PublishAsync(TaskMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class LoggingAgentEventPublisher : IAgentEventPublisher
{
    private readonly ILogger<LoggingAgentEventPublisher> _logger;
    private readonly PipelineWorkerOptions _options;
    private readonly ServiceBusSender? _sender;
    private readonly ServiceBusClient? _client;

    public LoggingAgentEventPublisher(
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<LoggingAgentEventPublisher> logger)
    {
        _logger = logger;
        _options = worker.Value;
        var cs = serviceBus.Value.ConnectionString;
        if (!_options.InProcessOnly && !string.IsNullOrWhiteSpace(cs))
        {
            _client = new ServiceBusClient(cs);
            _sender = _client.CreateSender(_options.EventsTopic);
        }
    }

    public async Task PublishEventAsync(AgentPipelineEvent pipelineEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "PIPELINE event {Kind} Stage={Stage} Status={Status} Message={Message}",
            pipelineEvent.Kind,
            pipelineEvent.Stage,
            pipelineEvent.Status,
            pipelineEvent.Message);

        if (_sender is null)
        {
            return;
        }

        var body = JsonSerializer.Serialize(pipelineEvent);
        await _sender
            .SendMessageAsync(
                new ServiceBusMessage(body)
                {
                    ContentType = "application/json",
                    MessageId = pipelineEvent.EventId,
                    CorrelationId = pipelineEvent.CorrelationId,
                    Subject = pipelineEvent.Kind
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class ServiceBusPipelineTaskPublisher : IPipelineTaskPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    public ServiceBusPipelineTaskPublisher(
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<PipelineWorkerOptions> worker)
    {
        var cs = serviceBus.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("ServiceBus:ConnectionString is required.");
        }

        _client = new ServiceBusClient(cs);
        _sender = _client.CreateSender(worker.Value.Topic);
    }

    public async Task PublishAsync(TaskMessage message, CancellationToken cancellationToken = default)
    {
        message.EnsureDefaults();
        var body = JsonSerializer.Serialize(message, JsonOptions);
        await _sender
            .SendMessageAsync(
                new ServiceBusMessage(body)
                {
                    ContentType = "application/json",
                    MessageId = message.LogicalMessageId,
                    CorrelationId = message.CorrelationId,
                    Subject = message.Stage
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Publishes AgentResult to Pipeline results topic (or no-ops when InProcessOnly).</summary>
public sealed class PipelineAwareAgentResultPublisher : IAgentResultPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<PipelineAwareAgentResultPublisher> _logger;
    private readonly PipelineWorkerOptions _pipeline;
    private readonly HealthyWorkerOptions _healthy;
    private readonly ServiceBusClient? _client;
    private readonly ServiceBusSender? _pipelineSender;
    private readonly ServiceBusSender? _healthySender;

    public PipelineAwareAgentResultPublisher(
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<PipelineWorkerOptions> pipeline,
        IOptions<HealthyWorkerOptions> healthy,
        ILogger<PipelineAwareAgentResultPublisher> logger)
    {
        _logger = logger;
        _pipeline = pipeline.Value;
        _healthy = healthy.Value;
        var cs = serviceBus.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            return;
        }

        _client = new ServiceBusClient(cs);
        if (!_pipeline.InProcessOnly)
        {
            _pipelineSender = _client.CreateSender(_pipeline.ResultsTopic);
        }

        if (_healthy.Enabled)
        {
            _healthySender = _client.CreateSender(_healthy.ResultsTopic);
        }
    }

    public async Task PublishResultAsync(AgentResult result, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "AgentResult Status={Status} Stage={Stage} TaskId={TaskId}",
            result.Status,
            result.Stage,
            result.TaskId);

        var body = JsonSerializer.Serialize(result, JsonOptions);
        var sbMessage = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = $"{result.RunId}:result",
            CorrelationId = result.CorrelationId,
            Subject = $"AgentResult:{result.Status}"
        };

        var isPipeline = !string.Equals(result.Stage, "HEALTHY", StringComparison.Ordinal);
        var sender = isPipeline ? _pipelineSender : _healthySender;
        if (sender is null)
        {
            return;
        }

        await sender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipelineSender is not null)
        {
            await _pipelineSender.DisposeAsync().ConfigureAwait(false);
        }

        if (_healthySender is not null)
        {
            await _healthySender.DisposeAsync().ConfigureAwait(false);
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
