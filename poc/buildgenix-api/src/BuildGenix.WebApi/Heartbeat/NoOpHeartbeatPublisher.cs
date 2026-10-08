namespace BuildGenix.WebApi.Heartbeat;

public sealed class NoOpHeartbeatPublisher : IHeartbeatPublisher
{
    public Task PublishPingAsync(HeartbeatMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishPongAsync(string body, string correlationId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
