using BuildGenix.Data.Entities;

namespace BuildGenix.Data.Repositories;

public enum WorkItemCreateOutcome
{
    Created,
    Duplicate,
}

public enum WorkItemImplementOutcome
{
    Started,
    AlreadyStarted,
    ActiveConflict,
    NotFound,
    InvalidRevision,
    SameWiDifferentRevisionOpen,
}

public sealed record WorkItemCreateResult(WorkItemCreateOutcome Outcome, WorkItemEntity? Item);

public sealed record WorkItemImplementResult(
    WorkItemImplementOutcome Outcome,
    WorkItemEntity? Item,
    string? ActiveWorkItemId = null,
    int? ActiveDisplayNumber = null);

public interface IWorkItemRepository
{
    Task EnsureTablesAsync(CancellationToken cancellationToken = default);

    Task<WorkItemEntity?> GetByIdAsync(string workItemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItemEntity>> ListByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<WorkItemCreateResult> TryCreateAsync(
        string projectId,
        string projectName,
        string userId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<WorkItemImplementResult> TryImplementAsync(
        string workItemId,
        int summaryRevision,
        CancellationToken cancellationToken = default);
}
