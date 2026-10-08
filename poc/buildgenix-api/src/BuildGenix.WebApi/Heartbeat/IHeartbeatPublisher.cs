namespace BuildGenix.WebApi.Heartbeat;

public interface IHeartbeatPublisher
{
    Task PublishPingAsync(HeartbeatMessage message, CancellationToken cancellationToken = default);

    Task PublishPongAsync(string body, string correlationId, CancellationToken cancellationToken = default);
}
