using AgentHub.Agents.Healthy;

namespace AgentHub.WebApi.Healthy;

public sealed class HealthyWorkerOptions
{
    public const string SectionName = "Workers:Healthy";

    public bool Enabled { get; set; } = true;

    public string Topic { get; set; } = HealthyAgentConstants.TaskTopic;

    public string Subscription { get; set; } = HealthyAgentConstants.Subscription;

    public string ResultsTopic { get; set; } = HealthyAgentConstants.ResultsTopic;

    public int MaxDeliveryCount { get; set; } = HealthyAgentConstants.DefaultMaxDeliveryCount;

    /// <summary>ISO 8601 duration, e.g. 00:02:00.</summary>
    public string MaxAutoLockRenewalDuration { get; set; } = "00:02:00";

    /// <summary>Deliberate pause inside the handler (milliseconds).</summary>
    public int DeliberatePauseMs { get; set; } = 250;

    public int MaxConcurrentCalls { get; set; } = 1;
}
