using AgentHub.Data.Repositories;
using AgentHub.WebApi.Heartbeat;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Tests;

public sealed class HeartbeatPingPongPathTests
{
    [Fact]
    public async Task HandlePingBody_publishes_pong_before_marking_idempotency()
    {
        var callOrder = new List<string>();
        var publisher = new FakeHeartbeatPublisher(onPublish: () => callOrder.Add("publish"));
        var idempotency = new FakeAgProcessedEventsRepository(onMark: () => callOrder.Add("mark"));
        var consumer = CreateConsumer(publisher, idempotency);

        const string pingBody = "{\"correlationId\":\"abc\",\"payload\":\"ping\"}";
        await consumer.HandlePingBodyAsync(
            pingBody,
            messageId: "msg-1",
            correlationId: "abc",
            cancellationToken: CancellationToken.None);

        Assert.Equal(new[] { "publish", "mark" }, callOrder);
        Assert.Single(publisher.Pongs);
        Assert.Equal("msg-1", idempotency.Marked[0]);
        Assert.Equal(pingBody + "|pong-by=agenthub-api", publisher.Pongs[0].Body);
        Assert.Equal("abc", publisher.Pongs[0].CorrelationId);
    }

    [Fact]
    public async Task HandlePingBody_does_not_mark_when_pong_publish_fails()
    {
        var publisher = new FakeHeartbeatPublisher(onPublish: () => throw new InvalidOperationException("pong failed"));
        var idempotency = new FakeAgProcessedEventsRepository();
        var consumer = CreateConsumer(publisher, idempotency);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.HandlePingBodyAsync(
                "ping",
                "msg-fail",
                "corr",
                CancellationToken.None));

        Assert.Empty(idempotency.Marked);
    }

    [Fact]
    public async Task HandlePingBody_still_succeeds_when_mark_returns_duplicate_after_pong()
    {
        var publisher = new FakeHeartbeatPublisher();
        var idempotency = new FakeAgProcessedEventsRepository { AlreadySeen = true };
        var consumer = CreateConsumer(publisher, idempotency);

        await consumer.HandlePingBodyAsync(
            "ping",
            "msg-dup",
            "corr",
            CancellationToken.None);

        Assert.Single(publisher.Pongs);
        Assert.Empty(idempotency.Marked);
    }

    private static HeartbeatPingConsumer CreateConsumer(
        IHeartbeatPublisher publisher,
        IAgProcessedEventsRepository idempotency)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
        {
            ConnectionString = string.Empty,
            PongMarker = "|pong-by=agenthub-api",
            EnableConsumer = false
        });

        return new HeartbeatPingConsumer(
            options,
            publisher,
            idempotency,
            NullLogger<HeartbeatPingConsumer>.Instance);
    }

    private sealed class FakeHeartbeatPublisher : IHeartbeatPublisher
    {
        private readonly Action? _onPublish;

        public FakeHeartbeatPublisher(Action? onPublish = null) => _onPublish = onPublish;

        public List<(string Body, string CorrelationId)> Pongs { get; } = [];

        public Task PublishPingAsync(HeartbeatMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishPongAsync(string body, string correlationId, CancellationToken cancellationToken = default)
        {
            _onPublish?.Invoke();
            Pongs.Add((body, correlationId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgProcessedEventsRepository : IAgProcessedEventsRepository
    {
        private readonly Action? _onMark;

        public FakeAgProcessedEventsRepository(Action? onMark = null) => _onMark = onMark;

        public bool AlreadySeen { get; init; }

        public List<string> Marked { get; } = [];

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AlreadySeen || Marked.Contains(messageId));

        public Task<bool> TryMarkProcessedAsync(
            string messageId,
            CancellationToken cancellationToken = default)
        {
            _onMark?.Invoke();
            if (AlreadySeen)
            {
                return Task.FromResult(false);
            }

            Marked.Add(messageId);
            return Task.FromResult(true);
        }
    }
}
