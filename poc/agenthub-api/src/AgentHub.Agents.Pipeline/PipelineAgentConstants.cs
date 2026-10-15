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

    /// <summary>Legacy alias — treated as IA_CODEGEN.</summary>
    public const string StageImplementation = "IMPLEMENTATION";

    /// <summary>Legacy — unit tests now run inside IA_CODEGEN; kept for older callers.</summary>
    public const string StageTesting = "TESTING";

    /// <summary>Legacy alias — treated as IA_PUSH_DEPLOY.</summary>
    public const string StageDeployDev = "DEPLOY_DEV";

    public const string StageIaPlan = "IA_PLAN";
    public const string StageIaCodegen = "IA_CODEGEN";
    public const string StageIaReview = "IA_REVIEW";
    public const string StageIaPushDeploy = "IA_PUSH_DEPLOY";

    public const string StageTaGenerate = "TA_GENERATE";
    public const string StageTaVerify = "TA_VERIFY";
    public const string StageTaReport = "TA_REPORT";
    public const string StageTaMerge = "TA_MERGE";

    public static readonly IReadOnlyList<string> SupportedStages =
    [
        StagePrd,
        StageSystemDesign,
        StageIaPlan,
        StageIaCodegen,
        StageIaReview,
        StageIaPushDeploy,
        StageTaGenerate,
        StageTaVerify,
        StageTaReport,
        StageTaMerge,
        StageImplementation,
        StageTesting,
        StageDeployDev
    ];

    public const int DefaultMaxDeliveryCount = 5;

    public const string IntentLlm = "llm.pipeline";
    public const string IntentArtifact = "artifact.pipeline";
    public const string IntentWorkspace = "workspace.pipeline";
    public const string IntentDeploy = "deploy.pipeline";
    public const string IntentIa = "ia.pipeline";
    public const string IntentTa = "ta.pipeline";

    public static string StateContextId(string projectId, string workItemId) =>
        MessageIds.StateContextId(projectId, workItemId);

    public static string LogicalMessageId(string taskId, int inputVersion) =>
        MessageIds.LogicalMessageId(taskId, inputVersion);

    public static string RunId(string taskId, int inputVersion, int deliveryCount) =>
        MessageIds.RunId(taskId, inputVersion, deliveryCount);

    public static string BlobPrefix(string projectId, string workItemId, int outputRevision) =>
        $"{projectId}/{workItemId}/v{outputRevision}/";
}
