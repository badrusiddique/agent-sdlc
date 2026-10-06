namespace BuildGenix.WebApi.Heartbeat;

/// <summary>
/// Pure ping→pong body transform used by the hosted consumer and unit tests.
/// </summary>
public static class HeartbeatMessageTransformer
{
    public static string AppendPongMarker(string pingBody, string marker)
    {
        ArgumentNullException.ThrowIfNull(pingBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        if (pingBody.Contains(marker, StringComparison.Ordinal))
        {
            return pingBody;
        }

        return pingBody + marker;
    }
}
