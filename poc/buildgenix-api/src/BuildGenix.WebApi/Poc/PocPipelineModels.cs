namespace BuildGenix.WebApi.Poc;

public sealed class PocPipelineStartRequest
{
    public string UserRequest { get; set; } = string.Empty;
    public string? StackProfile { get; set; }
    public string? ProjectMode { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public Dictionary<string, string>? BaselineArtifacts { get; set; }

    /// <summary>
    /// When true (default for POST /run), gates auto-approve so scripts keep working.
    /// Interactive chat uses POST /start which forces this false.
    /// </summary>
    public bool AutoApprove { get; set; } = true;
}

public sealed record PocPipelineRunResult(
    string ProjectId,
    string WorkItemId,
    string Status,
    string Stage,
    string? DeployedUrl,
    string AuditLogPath,
    IReadOnlyList<string> TransitionTrail,
    IReadOnlyList<string> EventTypes);

public sealed record PocPipelineStartResult(
    string ProjectId,
    string WorkItemId,
    string Status,
    string Stage,
    string Message);

public sealed class AuditLogDocument
{
    public string OriginalRequest { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string StackProfile { get; set; } = string.Empty;
    public string ProjectMode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FinalStatus { get; set; }
    public string? DeployedUrl { get; set; }
    public List<AuditTransition> Transitions { get; set; } = [];
}

public sealed class AuditTransition
{
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ArtifactPath { get; set; }
}

public sealed record PocPipelineStatusDto(
    string ProjectId,
    string WorkItemId,
    string Status,
    string CurrentStage,
    string? PendingGate,
    string? PendingGateLabel,
    string? DeployedUrl,
    string UserRequest,
    string StackProfile,
    string ProjectName,
    bool IsTerminal,
    bool Failed,
    string? ErrorMessage,
    IReadOnlyList<PocStageStripItem> Stages,
    DateTimeOffset UpdatedAt);

public sealed record PocStageStripItem(
    string Id,
    string Label,
    string State);

public sealed record PocPipelineEventDto(
    string EventId,
    string Stage,
    string Status,
    string EventType,
    string Message,
    string? TaskId,
    DateTimeOffset OccurredAt,
    string? ArtifactKey);

public sealed record PocArtifactListItem(
    string Key,
    int Revision,
    string BlobPath,
    string? Title);

public sealed record PocArtifactContentDto(
    string Key,
    string BlobPath,
    string ContentType,
    string Content);

public sealed record PocLogEntryDto(
    DateTimeOffset At,
    string Level,
    string Stage,
    string Source,
    string Message);

public sealed class PocApproveRequest
{
    public string? Feedback { get; set; }
}
