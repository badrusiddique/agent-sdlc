using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Heartbeat;
using BuildGenix.WebApi.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildGenix.WebApi.Tests;

public sealed class HeartbeatPingPongPathTests
{
    [Fact]
    public async Task HandlePingBody_marks_idempotency_appends_marker_and_publishes_pong()
    {
        var publisher = new FakeHeartbeatPublisher();
        var idempotency = new FakeWmProcessedEventsRepository();
        var options = Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true",
            PongMarker = "|pong-by=buildgenix-api",
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
            enqueuedAt: DateTimeOffset.UtcNow,
            cancellationToken: CancellationToken.None);

        Assert.Single(idempotency.Marked);
        Assert.Equal("msg-1", idempotency.Marked[0]);
        Assert.Single(publisher.Pongs);
        Assert.Equal(pingBody + "|pong-by=buildgenix-api", publisher.Pongs[0].Body);
        Assert.Equal("abc", publisher.Pongs[0].CorrelationId);
    }

    [Fact]
    public async Task HandlePingBody_skips_duplicate_message()
    {
        var publisher = new FakeHeartbeatPublisher();
        var idempotency = new FakeWmProcessedEventsRepository { AlreadySeen = true };
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
            DateTimeOffset.UtcNow,
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

    private sealed class FakeWmProcessedEventsRepository : IWmProcessedEventsRepository
    {
        public bool AlreadySeen { get; init; }

        public List<string> Marked { get; } = [];

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryMarkProcessedAsync(
            string messageId,
            DateTimeOffset enqueuedAtUtc,
            string source,
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
