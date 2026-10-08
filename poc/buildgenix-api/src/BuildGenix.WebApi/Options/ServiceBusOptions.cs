namespace BuildGenix.WebApi.Options;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string ConnectionString { get; set; } = string.Empty;

    public string PingTopic { get; set; } = "bg-heartbeat-ping";

    public string PingSubscription { get; set; } = "bg-api";

    public string PongTopic { get; set; } = "bg-heartbeat-pong";

    /// <summary>
    /// Marker appended by the ping consumer before publishing to the pong topic (D-C8).
    /// </summary>
    public string PongMarker { get; set; } = "|pong-by=buildgenix-api";

    /// <summary>
    /// When false, the hosted ping consumer is not started (useful for unit tests).
    /// </summary>
    public bool EnableConsumer { get; set; } = true;

    /// <summary>
    /// Local emulator HTTP health URL used by readiness when ConnectionString has UseDevelopmentEmulator=true.
    /// </summary>
    public string EmulatorHealthUrl { get; set; } = "http://127.0.0.1:5300/health";

    public bool UsesDevelopmentEmulator =>
        !string.IsNullOrWhiteSpace(ConnectionString)
        && ConnectionString.Contains("UseDevelopmentEmulator=true", StringComparison.OrdinalIgnoreCase);
}
