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

    /// <summary>POC / phase-0 context block (stack, request text, deploy action).</summary>
    public TaskMessageContext Context { get; set; } = new();

    /// <summary>Where this run writes outputs: {projectId}/{workItemId}/v{outputRevision}/.</summary>
    public string? BlobPrefix { get; set; }

    public int OutputRevision { get; set; } = 1;

    public int PipelineVersion { get; set; } = 1;

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

public sealed class TaskMessageContext
{
    public string? UserRequest { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectSlug { get; set; }
    public string? ProjectMode { get; set; }
    public string? StackProfile { get; set; }
    public string? TemplateId { get; set; }
    public string? Environment { get; set; } = "DEV";
    public string? DeploymentAction { get; set; }
    public string? WorkBranch { get; set; }
    public string? HeadCommit { get; set; }
    public string? WorkspaceRoot { get; set; }
    /// <summary>Live DEV URL from IA Push & Deploy — consumed by the Testing Agent.</summary>
    public string? DeployedUrl { get; set; }
    public string? PrUrl { get; set; }
    public int? PrNumber { get; set; }
    public Dictionary<string, string> BaselineArtifacts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>When true, agents chain to the next topic without waiting for BuildGenix HITL.</summary>
    public bool AutoApprove { get; set; } = true;

    /// <summary>POC: first TA_VERIFY injects a one-shot red probe for RouteBack.</summary>
    public bool SimulateTaRouteback { get; set; }

    /// <summary>Count of TA→IA RouteBack attempts already taken for this work item.</summary>
    public int RouteBackCount { get; set; }
}
