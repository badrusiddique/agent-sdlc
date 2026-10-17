using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentHub.Infrastructure.Messaging;

/// <summary>
/// Process-local competing-consumer topic bus for multi-agent BackgroundServices.
/// </summary>
public sealed class InMemoryAgentTaskBus : IAgentTaskBus, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<InMemoryAgentTaskBus> _logger;
    private readonly ConcurrentDictionary<string, TopicHub> _topics = new(StringComparer.Ordinal);

    public InMemoryAgentTaskBus(ILogger<InMemoryAgentTaskBus> logger) => _logger = logger;

    public Task PublishAsync(string topic, TaskMessage message, CancellationToken cancellationToken = default)
    {
        message.EnsureDefaults();
        var hub = _topics.GetOrAdd(topic, _ => new TopicHub());
        var envelope = new BusEnvelope(
            Guid.CreateVersion7().ToString("N"),
            JsonSerializer.Serialize(message, JsonOptions),
            message);
        if (!hub.Writer.TryWrite(envelope))
        {
            throw new InvalidOperationException($"InMemoryAgentTaskBus topic '{topic}' channel is closed.");
        }

        _logger.LogInformation(
            "AgentBus publish topic={Topic} stage={Stage} wi={WorkItemId} msgId={MessageId}",
            topic,
            message.Stage,
            message.WorkItemId,
            envelope.MessageId);
        return Task.CompletedTask;
    }

    public IAsyncDisposable Subscribe(
        string topic,
        string subscriptionName,
        Func<TaskMessage, string, int, CancellationToken, Task> handler)
    {
        var hub = _topics.GetOrAdd(topic, _ => new TopicHub());
        var cts = new CancellationTokenSource();
        var reader = hub.Reader;
        var pump = Task.Run(async () =>
        {
            _logger.LogInformation(
                "AgentBus subscribe topic={Topic} subscription={Subscription}",
                topic,
                subscriptionName);
            await foreach (var envelope in reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await handler(envelope.Message, envelope.MessageId, 1, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "AgentBus handler fault topic={Topic} subscription={Subscription} msgId={MessageId}",
                        topic,
                        subscriptionName,
                        envelope.MessageId);
                }
            }
        }, cts.Token);

        return new Subscription(cts, pump);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record BusEnvelope(string MessageId, string Body, TaskMessage Message);

    private sealed class TopicHub
    {
        private readonly Channel<BusEnvelope> _channel =
            Channel.CreateUnbounded<BusEnvelope>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

        public ChannelWriter<BusEnvelope> Writer => _channel.Writer;
        public ChannelReader<BusEnvelope> Reader => _channel.Reader;
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _pump;

        public Subscription(CancellationTokenSource cts, Task pump)
        {
            _cts = cts;
            _pump = pump;
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            _cts.Dispose();
        }
    }
}
