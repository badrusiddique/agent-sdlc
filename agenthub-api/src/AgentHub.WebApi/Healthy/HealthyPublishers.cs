using System.Text.Json;
using AgentHub.Agents.Healthy;
using AgentHub.Agents.Healthy.Abstractions;
using AgentHub.Agents.Healthy.Models;
using AgentHub.WebApi.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Healthy;

public interface IHealthyTaskPublisher
{
    Task PublishAsync(TaskMessage message, CancellationToken cancellationToken = default);
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
            Subject = HealthyAgentConstants.StageCode
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
