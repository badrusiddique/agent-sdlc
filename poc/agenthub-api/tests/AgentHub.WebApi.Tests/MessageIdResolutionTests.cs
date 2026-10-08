using AgentHub.Hosting;
using Azure.Messaging.ServiceBus;

namespace AgentHub.WebApi.Tests;

public sealed class MessageIdResolutionTests
{
    [Fact]
    public void TryResolveMessageId_uses_service_bus_message_id_when_present()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "sb-id");
        var ok = HealthyAgentWorker.TryResolveMessageId(message, "{}", out var id, out var reason);
        Assert.True(ok);
        Assert.Equal("sb-id", id);
        Assert.Null(reason);
    }

    [Fact]
    public void TryResolveMessageId_falls_back_to_logical_message_id_from_body()
    {
        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t9","correlationId":"c","inputVersion":2}
            """;
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body),
            messageId: null);
        var ok = HealthyAgentWorker.TryResolveMessageId(message, body, out var id, out var reason);
        Assert.True(ok);
        Assert.Equal("t9:2", id);
        Assert.Null(reason);
    }

    [Fact]
    public void TryResolveMessageId_fails_when_neither_source_available()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: null);
        var ok = HealthyAgentWorker.TryResolveMessageId(message, "{}", out var id, out var reason);
        Assert.False(ok);
        Assert.Equal(string.Empty, id);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
