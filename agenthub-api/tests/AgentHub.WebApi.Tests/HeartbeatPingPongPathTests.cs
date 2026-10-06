using AgentHub.Data.Repositories;
using AgentHub.WebApi.Heartbeat;
using AgentHub.WebApi.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Tests;

public sealed class HeartbeatPingPongPathTests
{
    [Fact]
    public async Task HandlePingBody_marks_idempotency_appends_marker_and_publishes_pong()
    {
        var publisher = new FakeHeartbeatPublisher();
        var idempotency = new FakeAgProcessedEventsRepository();
        var options = Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true",
            PongMarker = "|pong-by=agenthub-api",
            EnableConsumer = false
        });

        var consumer = new HeartbeatPingConsumer(
            options,
            publisher,
            idempotency,
            NullLogger<HeartbeatPingConsumer>.Instance);

        const string pingBody = "{\"correlationId\":\"abc\",\"payload\":\"ping\"}";
        await consumer.HandlePingBodyAsync(
            pingBody,
            messageId: "msg-1",
            correlationId: "abc",
            cancellationToken: CancellationToken.None);

        Assert.Single(idempotency.Marked);
        Assert.Equal("msg-1", idempotency.Marked[0]);
        Assert.Single(publisher.Pongs);
        Assert.Equal(pingBody + "|pong-by=agenthub-api", publisher.Pongs[0].Body);
        Assert.Equal("abc", publisher.Pongs[0].CorrelationId);
    }

    [Fact]
    public async Task HandlePingBody_skips_duplicate_message()
    {
        var publisher = new FakeHeartbeatPublisher();
        var idempotency = new FakeAgProcessedEventsRepository { AlreadySeen = true };
        var options = Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true",
            EnableConsumer = false
        });

        var consumer = new HeartbeatPingConsumer(
            options,
            publisher,
            idempotency,
            NullLogger<HeartbeatPingConsumer>.Instance);

        await consumer.HandlePingBodyAsync(
            "ping",
            "msg-dup",
            "corr",
            CancellationToken.None);

        Assert.Empty(publisher.Pongs);
    }

    private sealed class FakeHeartbeatPublisher : IHeartbeatPublisher
    {
        public List<(string Body, string CorrelationId)> Pongs { get; } = [];

        public Task PublishPingAsync(HeartbeatMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishPongAsync(string body, string correlationId, CancellationToken cancellationToken = default)
        {
            Pongs.Add((body, correlationId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgProcessedEventsRepository : IAgProcessedEventsRepository
    {
        public bool AlreadySeen { get; init; }

        public List<string> Marked { get; } = [];

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AlreadySeen || Marked.Contains(messageId));

        public Task<bool> TryMarkProcessedAsync(
            string messageId,
            CancellationToken cancellationToken = default)
        {
            if (AlreadySeen)
            {
                return Task.FromResult(false);
            }

            Marked.Add(messageId);
            return Task.FromResult(true);
        }
    }
}
