using AgentHub.Core.Messaging;

namespace AgentHub.Agents.Pipeline;

/// <summary>Identity and stage codes for the POC pipeline walker (Healthy-shaped).</summary>
public static class PipelineAgentConstants
{
    public const string AgentName = "agenthub-pipeline-agent";
    public const string AgentId = "agenthub-pipeline-agent";
    public const string TaskTopic = "agenthub.pipeline.agent";
    public const string Subscription = "agenthub-pipeline";
    public const string ResultsTopic = "buildgenix.agent-results";
    public const string EventsTopic = "buildgenix.agent-events";

    public const string StagePrd = "PRD";
    public const string StageSystemDesign = "SYSTEM_DESIGN";
    public const string StageImplementation = "IMPLEMENTATION";
    public const string StageTesting = "TESTING";
    public const string StageDeployDev = "DEPLOY_DEV";

    public static readonly IReadOnlyList<string> SupportedStages =
    [
        StagePrd,
        StageSystemDesign,
        StageImplementation,
        StageTesting,
        StageDeployDev
    ];

    public const int DefaultMaxDeliveryCount = 5;

    public const string IntentLlm = "llm.pipeline";
    public const string IntentArtifact = "artifact.pipeline";
    public const string IntentWorkspace = "workspace.pipeline";
    public const string IntentDeploy = "deploy.pipeline";

    public static string StateContextId(string projectId, string workItemId) =>
        MessageIds.StateContextId(projectId, workItemId);

    public static string LogicalMessageId(string taskId, int inputVersion) =>
        MessageIds.LogicalMessageId(taskId, inputVersion);

    public static string RunId(string taskId, int inputVersion, int deliveryCount) =>
        MessageIds.RunId(taskId, inputVersion, deliveryCount);

    public static string BlobPrefix(string projectId, string workItemId, int outputRevision) =>
        $"{projectId}/{workItemId}/v{outputRevision}/";
}
