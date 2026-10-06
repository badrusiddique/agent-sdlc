namespace AgentHub.Core.Messaging;

/// <summary>Shared identity helpers for TaskMessage / Service Bus MessageId (D-I2-4).</summary>
public static class MessageIds
{
    public static string LogicalMessageId(string taskId, int inputVersion) =>
        $"{taskId}:{inputVersion}";

    public static string RunId(string taskId, int inputVersion, int deliveryCount) =>
        $"{taskId}:{inputVersion}:{deliveryCount}";

    public static string StateContextId(string projectId, string workItemId) =>
        $"{projectId}:{workItemId}";
}
