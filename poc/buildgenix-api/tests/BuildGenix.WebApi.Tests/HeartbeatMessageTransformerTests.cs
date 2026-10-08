using BuildGenix.WebApi.Heartbeat;

namespace BuildGenix.WebApi.Tests;

public sealed class HeartbeatMessageTransformerTests
{
    [Fact]
    public void AppendPongMarker_appends_marker_once()
    {
        const string marker = "|pong-by=buildgenix-api";
        var once = HeartbeatMessageTransformer.AppendPongMarker("{\"payload\":\"ping\"}", marker);
        var twice = HeartbeatMessageTransformer.AppendPongMarker(once, marker);

        Assert.Equal("{\"payload\":\"ping\"}|pong-by=buildgenix-api", once);
        Assert.Equal(once, twice);
    }
}
