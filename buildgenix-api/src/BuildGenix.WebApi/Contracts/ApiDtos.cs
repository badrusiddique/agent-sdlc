namespace BuildGenix.WebApi.Contracts;

public sealed record ProjectSummaryDto(
    string ProjectId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    int? WorkItemCount = null,
    int? WaitingCount = null,
    int? BlockedCount = null);

public sealed record ProjectDetailDto(
    string ProjectId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    string CreatedBy);

public sealed record ProjectListResponse(IReadOnlyList<ProjectSummaryDto> Items);

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record CreateProjectResponse(string ProjectId, string Name, DateTimeOffset CreatedAt);

public sealed record PatchProjectRequest(string? Description, object? Settings);

public sealed record PendingActionDto(
    string Kind,
    string WorkItemId,
    string ProjectId,
    string? ProjectName,
    string? WorkItemTitle,
    string? DisplayNumber,
    string? GateId,
    string? StageCode,
    string Summary,
    DateTimeOffset WaitingSince,
    string? DeepLinkPath);

public sealed record PendingActionListResponse(int TotalCount, IReadOnlyList<PendingActionDto> Items);

public sealed record HomeActivityItemDto(
    string EventId,
    DateTimeOffset OccurredAt,
    string ActorLabel,
    string Description,
    string? WorkItemId,
    string? ProjectId);

public sealed record HomeActivityResponse(IReadOnlyList<HomeActivityItemDto> Items);

public sealed record WorkItemListItemDto(
    string WorkItemId,
    int DisplayNumber,
    string? Title,
    string Status,
    string CurrentStage,
    int? FeatureCount,
    DateTimeOffset? WaitingSince,
    object? LifecycleSummary);

public sealed record WorkItemListResponse(IReadOnlyList<WorkItemListItemDto> Items, string? ContinuationToken = null);

public sealed record WorkItemDetailDto(
    string WorkItemId,
    string ProjectId,
    string ProjectName,
    int DisplayNumber,
    string? Title,
    string Status,
    string CurrentStage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? HumanMessage,
    string? BlobPrefix,
    object? TabCounts = null,
    object? TelemetryBrief = null,
    int? LoopCount = null);

public sealed record CreateWorkItemRequest(string? Title, string? IdempotencyKey);

public sealed record CreateWorkItemResponse(
    string WorkItemId,
    int DisplayNumber,
    string Status,
    string ProjectId,
    string ConversationThreadId);

public sealed record ExecuteWorkItemRequest(int SummaryRevision, string? SummaryArtifactId);

public sealed record ExecuteWorkItemResponse(
    string WorkItemId,
    string Status,
    string CurrentStage,
    int BoundSummaryRevision,
    bool AlreadyStarted);

public sealed record AnswerHumanActionRequest(string Answer);

public sealed record PauseWorkItemRequest(string? Reason);

public sealed record StopWorkItemRequest(string? Reason);

public sealed record ProgressResponse(
    IReadOnlyList<object> Stages,
    object? SelectedStage,
    IReadOnlyList<PendingActionDto> WaitingOnYou,
    IReadOnlyList<object> StillToCome,
    object? ApprovalsBrief);

public sealed record WorkItemTelemetryResponse(
    IReadOnlyList<object> Runs,
    long TotalInputTokens,
    long TotalOutputTokens,
    double TotalCostUsd,
    long TotalDurationMs,
    int RunCount);

public sealed record ConversationThreadSummaryDto(
    string ThreadId,
    string Kind,
    int MessageCount,
    string? EndedSummary,
    DateTimeOffset? LastActivityAt);

public sealed record ConversationThreadListResponse(IReadOnlyList<ConversationThreadSummaryDto> Items);

public sealed record ConversationMessageDto(
    string MessageId,
    int Sequence,
    string Role,
    string Content,
    string? ContentBlobPath,
    DateTimeOffset CreatedAt,
    int? InputTokens,
    int? OutputTokens);

public sealed record AttachmentDto(
    string AttachmentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string BlobPath,
    DateTimeOffset CreatedAt);

public sealed record ConversationDetailResponse(
    string ThreadId,
    IReadOnlyList<ConversationMessageDto> Messages,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record PostConversationMessageRequest(string Content, string? ClientMessageId);

public sealed record PostConversationMessageResponse(
    ConversationMessageDto UserMessage,
    IReadOnlyList<ConversationMessageDto> AssistantMessages,
    int? ContextVersion);

public sealed record ProduceIntakeSummaryRequest(bool? Force);

public sealed record IntakeSummaryRefDto(
    int Revision,
    string ArtifactId,
    string BlobPath,
    DateTimeOffset ProducedAt);

public sealed record IntakeSummaryDetailDto(
    int Revision,
    string ArtifactId,
    string? Content,
    string? ContentUrl,
    DateTimeOffset ProducedAt);

public sealed record ArtifactListItemDto(
    string ArtifactId,
    string ArtifactKey,
    string StageCode,
    int Revision,
    string? Title,
    string? Summary,
    string Status,
    bool IsCurrent,
    string? FeatureId,
    bool? AwaitingApproval,
    string? GateId);

public sealed record ArtifactListResponse(IReadOnlyList<ArtifactListItemDto> Items);

public sealed record ArtifactDetailDto(
    string ArtifactId,
    string ArtifactKey,
    string StageCode,
    int Revision,
    string? Title,
    string? Summary,
    string Status,
    bool IsCurrent,
    string BlobContainer,
    string BlobPath,
    string ContentType,
    long? SizeBytes,
    DateTimeOffset CreatedAt,
    object? SupportingArtifacts,
    string? LinkedGateId);

public sealed record ArtifactContentResponse(string Url, DateTimeOffset ExpiresAt);

public sealed record GateDto(
    string GateId,
    int PipelineVersion,
    string StageCode,
    string? GateKind,
    string? AssumptionId,
    string Status,
    DateTimeOffset RequestedAt,
    string? ArtifactIdShown,
    int? ArtifactRevisionShown,
    object? SupportingArtifacts,
    DateTimeOffset? ResolvedAt,
    string? Feedback);

public sealed record GateListResponse(IReadOnlyList<GateDto> Items);

public sealed record ApprovalDto(
    string ApprovalId,
    string GateId,
    int PipelineVersion,
    string StageCode,
    string Decision,
    string DecisionBy,
    DateTimeOffset DecisionAt,
    string? ArtifactId,
    int? ArtifactRevision,
    string? Feedback);

public sealed record ApprovalListResponse(IReadOnlyList<ApprovalDto> Items);

public sealed record GateDecisionRequest(
    string Decision,
    string ArtifactId,
    int ArtifactRevision,
    string? Feedback);

public sealed record GateDecisionResponse(
    GateDto Gate,
    ApprovalDto Approval,
    string WorkItemStatus,
    string CurrentStage);

public sealed record AssumptionDto(string AssumptionId, string Statement, string? Status, string? BlocksStage);

public sealed record AssumptionListResponse(IReadOnlyList<AssumptionDto> Items);

public sealed record UpdateFindingStateRequest(string ArtifactKey, string Disposition, string? Note);

public sealed record FindingStateDto(
    string FindingId,
    string ArtifactKey,
    string Disposition,
    string? Note);

public sealed record ActivityItemDto(
    string EventId,
    DateTimeOffset OccurredAt,
    string ActorType,
    string ActorLabel,
    string Title,
    string? Detail,
    string? StageCode,
    string? FeatureLabel,
    long? CostTokens,
    string? RunId,
    bool? Highlight);

public sealed record ActivityListResponse(
    int? TotalCount,
    long? TotalTokens,
    int? RunCount,
    IReadOnlyList<ActivityItemDto> Items);

public sealed record ActivityDetailDto(
    ActivityItemDto Event,
    string? RunId,
    string? LogPreview,
    string? ArtifactId,
    int? ArtifactRevision);

public sealed record RunLogLineDto(long OffsetMs, int Seq, string? Level, string Message, string? DetailJson);

public sealed record RunLogResponse(string RunId, IReadOnlyList<RunLogLineDto> Lines);

public sealed record ProposalDto(
    string ProposalId,
    string Kind,
    string Title,
    string Summary,
    long? EstimatedTokens,
    string Impact,
    string Status,
    string? BlockedOnWorkItemId);

public sealed record ProposalListResponse(IReadOnlyList<ProposalDto> Items);

public sealed record ProposalDecisionRequest(string Decision);

public sealed record BulkProposalDecisionRequest(string Decision, IReadOnlyList<string>? ProposalIds);

public sealed record InterventionDto(
    string InterventionId,
    DateTimeOffset OccurredAt,
    string UserText,
    string Classification,
    string EffectSummary,
    long? CostTokens);

public sealed record InterventionListResponse(IReadOnlyList<InterventionDto> Items);

public sealed record SteeringCostResponse(
    long TotalTokensOnWorkItem,
    long HumanCausedTokens,
    long PlatformCausedTokens,
    int LoopCount,
    object? PendingProjection);

public sealed record CreateSignOffRequest(string ArtifactId, int ArtifactRevision, bool Accepted);

public sealed record SignOffDto(
    string SignOffId,
    string ArtifactId,
    int ArtifactRevision,
    bool Accepted,
    string DecisionBy,
    DateTimeOffset DecisionAt);

public sealed record AcceptedResponse(bool Processed);
