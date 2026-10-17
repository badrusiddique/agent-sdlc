namespace AgentHub.Core.Messaging;

/// <summary>
/// One Service Bus topic per agent process (PRD / SystemDesign / IA / TA / Deploy).
/// BuildGenix publishes only the first PRD task; agents publish the next topic themselves.
/// </summary>
public static class AgentTopics
{
    public const string Prd = "agenthub.agent.prd";
    public const string SystemDesign = "agenthub.agent.system-design";
    public const string Ia = "agenthub.agent.ia";
    public const string Ta = "agenthub.agent.ta";
    public const string Deploy = "agenthub.agent.deploy";

    public const string PrdSubscription = "prd-worker";
    public const string SystemDesignSubscription = "system-design-worker";
    public const string IaSubscription = "ia-worker";
    public const string TaSubscription = "ta-worker";
    public const string DeploySubscription = "deploy-worker";

    /// <summary>Maps a pipeline stage code to the agent topic that owns it.</summary>
    public static string TopicForStage(string stage) =>
        stage.ToUpperInvariant() switch
        {
            "PRD" => Prd,
            "SYSTEM_DESIGN" => SystemDesign,
            "IA_PLAN" or "IA_CODEGEN" or "IA_REVIEW" or "IA_PUSH_DEPLOY"
                or "IMPLEMENTATION" or "TESTING" or "DEPLOY_DEV" => Ia,
            "TA_GENERATE" or "TA_VERIFY" or "TA_REPORT" or "TA_MERGE" => Ta,
            "DEPLOY_SANDBOX" => Deploy,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage for topic map.")
        };

    public static string SubscriptionForTopic(string topic) =>
        topic switch
        {
            Prd => PrdSubscription,
            SystemDesign => SystemDesignSubscription,
            Ia => IaSubscription,
            Ta => TaSubscription,
            Deploy => DeploySubscription,
            _ => "default-worker"
        };
}
