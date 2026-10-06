using System.Text.Json.Serialization;

namespace AgentHub.Agents.Healthy.Models;

/// <summary>
/// Frozen TaskMessage surface for HEALTHY (D-I2-4). Production agents reuse this shape.
/// Service Bus MessageId must equal <see cref="LogicalMessageId"/>.
/// </summary>
public sealed class TaskMessage
{
    public string WorkItemId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Stage { get; set; } = HealthyAgentConstants.StageCode;
    public string TaskId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public int InputVersion { get; set; } = 1;

    /// <summary>Alias of InputVersion for callers that say Version.</summary>
    public int Version
    {
        get => InputVersion;
        set => InputVersion = value;
    }

    /// <summary>Stable context key. Default/recommended: {projectId}:{workItemId}.</summary>
    public string? StateContextId { get; set; }

    public TaskMessageConfig Config { get; set; } = new();

    public Dictionary<string, string> Artifacts { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public string LogicalMessageId =>
        HealthyAgentConstants.LogicalMessageId(TaskId, InputVersion);

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(Stage))
        {
            Stage = HealthyAgentConstants.StageCode;
        }

        if (string.IsNullOrWhiteSpace(StateContextId)
            && !string.IsNullOrWhiteSpace(ProjectId)
            && !string.IsNullOrWhiteSpace(WorkItemId))
        {
            StateContextId = HealthyAgentConstants.StateContextId(ProjectId, WorkItemId);
        }

        if (InputVersion < 1)
        {
            InputVersion = 1;
        }
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(WorkItemId)) errors.Add("WorkItemId is required.");
        if (string.IsNullOrWhiteSpace(ProjectId)) errors.Add("ProjectId is required.");
        if (string.IsNullOrWhiteSpace(TaskId)) errors.Add("TaskId is required.");
        if (string.IsNullOrWhiteSpace(CorrelationId)) errors.Add("CorrelationId is required.");
        if (string.IsNullOrWhiteSpace(Stage)) errors.Add("Stage is required.");
        if (!string.Equals(Stage, HealthyAgentConstants.StageCode, StringComparison.Ordinal))
        {
            errors.Add($"Stage must be {HealthyAgentConstants.StageCode} for this agent.");
        }

        if (InputVersion < 1) errors.Add("InputVersion must be >= 1.");
        return errors;
    }
}

public sealed class TaskMessageConfig
{
    public string? ModelId { get; set; }
    public string? ReasoningEffort { get; set; }
}
