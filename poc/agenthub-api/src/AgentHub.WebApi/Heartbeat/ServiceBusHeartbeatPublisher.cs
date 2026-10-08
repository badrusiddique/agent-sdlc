using System.Text.Json;
using Azure.Messaging.ServiceBus;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Heartbeat;

public sealed class ServiceBusHeartbeatPublisher : IHeartbeatPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _pingSender;
    private readonly ServiceBusSender _pongSender;
    private readonly ServiceBusOptions _options;

    public ServiceBusHeartbeatPublisher(IOptions<ServiceBusOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("ServiceBus:ConnectionString is required.");
        }

        _client = new ServiceBusClient(_options.ConnectionString);
        _pingSender = _client.CreateSender(_options.PingTopic);
        _pongSender = _client.CreateSender(_options.PongTopic);
    }

    public async Task PublishPingAsync(HeartbeatMessage message, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(message, JsonOptions);
        var sbMessage = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
            MessageId = message.CorrelationId,
            Subject = "ah-heartbeat-ping"
        };

        await _pingSender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishPongAsync(string body, string correlationId, CancellationToken cancellationToken = default)
    {
        var sbMessage = new ServiceBusMessage(body)
        {
            ContentType = "text/plain",
            CorrelationId = correlationId,
            MessageId = Guid.NewGuid().ToString("N"),
            Subject = "ah-heartbeat-pong"
        };

        await _pongSender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _pingSender.DisposeAsync().ConfigureAwait(false);
        await _pongSender.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
