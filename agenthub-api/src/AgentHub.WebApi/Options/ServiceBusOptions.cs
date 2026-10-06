namespace AgentHub.WebApi.Options;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string ConnectionString { get; set; } = string.Empty;

    public string PingTopic { get; set; } = "ah-heartbeat-ping";

    public string PingSubscription { get; set; } = "ah-api";

    public string PongTopic { get; set; } = "ah-heartbeat-pong";

    /// <summary>
    /// Marker appended by the ping consumer before publishing to the pong topic.
    /// </summary>
    public string PongMarker { get; set; } = "|pong-by=agenthub-api";

    /// <summary>
    /// When false, the hosted ping consumer is not started (useful for unit tests).
    /// </summary>
    public bool EnableConsumer { get; set; } = true;
}
