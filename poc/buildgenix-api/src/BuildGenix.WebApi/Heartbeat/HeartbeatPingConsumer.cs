using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace BuildGenix.WebApi.Heartbeat;

/// <summary>
/// Same-API hosted consumer: reads <c>bg-heartbeat-ping</c>, appends marker, publishes <c>bg-heartbeat-pong</c>.
/// D-C1: publish pong → mark WmProcessedEvents → complete SB message.
/// </summary>
public sealed class HeartbeatPingConsumer : BackgroundService
{
    private readonly IHeartbeatPublisher _publisher;
    private readonly IWmProcessedEventsRepository _idempotency;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<HeartbeatPingConsumer> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public HeartbeatPingConsumer(
        IOptions<ServiceBusOptions> options,
        IHeartbeatPublisher publisher,
        IWmProcessedEventsRepository idempotency,
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
            _logger.LogError(
                "Heartbeat ping consumer cannot start: ServiceBus:ConnectionString missing while EnableConsumer=true. Host stays up.");
            return;
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
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken)
    {
        var pongBody = HeartbeatMessageTransformer.AppendPongMarker(body, _options.PongMarker);
        await _publisher
            .PublishPongAsync(pongBody, correlationId ?? messageId, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Heartbeat ping {MessageId} pong published to {PongTopic} (D-C1 before mark)",
            messageId,
            _options.PongTopic);

        var firstSeen = await _idempotency
            .TryMarkProcessedAsync(messageId, enqueuedAt, "bg-heartbeat-ping", cancellationToken)
            .ConfigureAwait(false);

        if (!firstSeen)
        {
            _logger.LogInformation(
                "Heartbeat ping {MessageId} idempotency mark already present after pong (duplicate)",
                messageId);
        }
        else
        {
            _logger.LogDebug(
                "Heartbeat ping {MessageId} marked in WmProcessedEvents after pong publish",
                messageId);
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        if (string.IsNullOrWhiteSpace(args.Message.MessageId))
        {
            _logger.LogWarning("Heartbeat ping missing MessageId; dead-lettering (never invent GUID for dedup)");
            await args
                .DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "MissingMessageId",
                    deadLetterErrorDescription: "Service Bus MessageId is required for D-C1 dedup.",
                    cancellationToken: args.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var messageId = args.Message.MessageId;
        var enqueuedAt = args.Message.EnqueuedTime == default
            ? DateTimeOffset.UtcNow
            : args.Message.EnqueuedTime;

        try
        {
            await HandlePingBodyAsync(
                    body,
                    messageId,
                    args.Message.CorrelationId,
                    enqueuedAt,
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
