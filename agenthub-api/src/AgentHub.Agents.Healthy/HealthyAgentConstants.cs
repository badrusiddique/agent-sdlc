namespace AgentHub.Agents.Healthy;

/// <summary>Locked identity for the HEALTHY harness agent (D-I2-2).</summary>
public static class HealthyAgentConstants
{
    public const string AgentName = "agenthub-health-agent";
    public const string AgentId = "agenthub-health-agent";
    public const string StageCode = "HEALTHY";
    public const string TaskTopic = "sdlc-tasks-healthy";
    public const string Subscription = "healthy-tasks";
    public const string ResultsTopic = "sdlc-results";
    public const string EventsTopic = "sdlc-events";
    public const string ArtifactFileName = "healthy-result.json";
    public const string ArtifactKey = "healthy";

    public const string IntentLlm = "llm.healthy";
    public const string IntentArtifact = "artifact.healthy";
    public const int OrdinalLlm = 100;
    public const int OrdinalArtifact = 200;

    public const int DefaultMaxDeliveryCount = 5;

    /// <summary>
    /// D-I2-4: stable context key for blob/table correlation.
    /// Choice: <c>{projectId}:{workItemId}</c> (not the blob path prefix).
    /// </summary>
    public static string StateContextId(string projectId, string workItemId) =>
        $"{projectId}:{workItemId}";

    public static string LogicalMessageId(string taskId, int inputVersion) =>
        $"{taskId}:{inputVersion}";

    public static string BlobPath(string projectId, string workItemId, int inputVersion) =>
        $"{projectId}/{workItemId}/v{inputVersion}/{ArtifactFileName}";

    public static string RunId(string taskId, int inputVersion, int deliveryCount) =>
        $"{taskId}:{inputVersion}:{deliveryCount}";
}
