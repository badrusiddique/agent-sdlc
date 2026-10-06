using AgentHub.Data.Repositories;
using AgentHub.Infrastructure.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Heartbeat;

/// <summary>
/// Same-API hosted consumer: reads <c>ah-heartbeat-ping</c>, appends marker, publishes <c>ah-heartbeat-pong</c>.
/// D-C1: publish pong → mark AgProcessedEvents → complete SB message.
/// </summary>
public sealed class HeartbeatPingConsumer : BackgroundService
{
    private readonly IHeartbeatPublisher _publisher;
    private readonly IAgProcessedEventsRepository _idempotency;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<HeartbeatPingConsumer> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public HeartbeatPingConsumer(
        IOptions<ServiceBusOptions> options,
        IHeartbeatPublisher publisher,
        IAgProcessedEventsRepository idempotency,
        ILogger<HeartbeatPingConsumer> logger)
    {
        _options = options.Value;
        _publisher = publisher;
        _idempotency = idempotency;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableConsumer)
        {
            _logger.LogInformation("Heartbeat ping consumer disabled by configuration.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("ServiceBus:ConnectionString is required when EnableConsumer is true.");
        }

        _client = new ServiceBusClient(_options.ConnectionString);
        _processor = _client.CreateProcessor(
            _options.PingTopic,
            _options.PingSubscription,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
                PrefetchCount = 0
            });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Heartbeat ping consumer listening on {Topic}/{Subscription}",
            _options.PingTopic,
            _options.PingSubscription);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown
        }
    }

    internal async Task HandlePingBodyAsync(
        string body,
        string messageId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var pongBody = HeartbeatMessageTransformer.AppendPongMarker(body, _options.PongMarker);
        await _publisher
            .PublishPongAsync(pongBody, correlationId ?? messageId, cancellationToken)
            .ConfigureAwait(false);

        var firstSeen = await _idempotency
            .TryMarkProcessedAsync(messageId, cancellationToken)
            .ConfigureAwait(false);

        if (!firstSeen)
        {
            _logger.LogInformation(
                "Heartbeat ping {MessageId} pong published; idempotency mark already present (duplicate)",
                messageId);
        }

        _logger.LogInformation(
            "Heartbeat ping {MessageId} forwarded to {PongTopic}",
            messageId,
            _options.PongTopic);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var messageId = args.Message.MessageId ?? Guid.NewGuid().ToString("N");

        try
        {
            await HandlePingBodyAsync(
                    body,
                    messageId,
                    args.Message.CorrelationId,
                    args.CancellationToken)
                .ConfigureAwait(false);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing heartbeat ping {MessageId}", messageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus processor error. Source={ErrorSource} Entity={EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await _processor.DisposeAsync().ConfigureAwait(false);
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
