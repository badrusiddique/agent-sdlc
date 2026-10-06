namespace BuildGenix.WebApi.Heartbeat;

public sealed record HeartbeatMessage(
    string CorrelationId,
    DateTimeOffset SentAtUtc,
    string Payload);
