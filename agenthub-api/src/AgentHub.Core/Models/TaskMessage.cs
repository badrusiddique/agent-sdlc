using System.Text.Json.Serialization;
using AgentHub.Core.Messaging;

namespace AgentHub.Core.Models;

/// <summary>
/// Frozen TaskMessage surface (D-I2-4 / contract 1.3.0 field set used in Phase 0).
/// Stage-specific rules belong in the agent handler, not here.
/// Service Bus MessageId must equal <see cref="LogicalMessageId"/>.
/// </summary>
public sealed class TaskMessage
{
    public string WorkItemId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
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
        MessageIds.LogicalMessageId(TaskId, InputVersion);

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(StateContextId)
            && !string.IsNullOrWhiteSpace(ProjectId)
            && !string.IsNullOrWhiteSpace(WorkItemId))
        {
            StateContextId = MessageIds.StateContextId(ProjectId, WorkItemId);
        }

        if (InputVersion < 1)
        {
            InputVersion = 1;
        }
    }

    /// <summary>Common required-field validation. Does not enforce a specific Stage code.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(WorkItemId)) errors.Add("WorkItemId is required.");
        if (string.IsNullOrWhiteSpace(ProjectId)) errors.Add("ProjectId is required.");
        if (string.IsNullOrWhiteSpace(TaskId)) errors.Add("TaskId is required.");
        if (string.IsNullOrWhiteSpace(CorrelationId)) errors.Add("CorrelationId is required.");
        if (string.IsNullOrWhiteSpace(Stage)) errors.Add("Stage is required.");
        if (InputVersion < 1) errors.Add("InputVersion must be >= 1.");
        return errors;
    }
}

public sealed class TaskMessageConfig
{
    public string? ModelId { get; set; }
    public string? ReasoningEffort { get; set; }
}
