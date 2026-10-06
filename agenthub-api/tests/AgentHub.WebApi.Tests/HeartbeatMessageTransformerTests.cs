using AgentHub.WebApi.Heartbeat;

namespace AgentHub.WebApi.Tests;

public sealed class HeartbeatMessageTransformerTests
{
    [Fact]
    public void AppendPongMarker_appends_marker_once()
    {
        const string marker = "|pong-by=agenthub-api";
        var once = HeartbeatMessageTransformer.AppendPongMarker("{\"payload\":\"ping\"}", marker);
        var twice = HeartbeatMessageTransformer.AppendPongMarker(once, marker);

        Assert.Equal("{\"payload\":\"ping\"}|pong-by=agenthub-api", once);
        Assert.Equal(once, twice);
    }
}
