using BuildGenix.Data.Entities;
using BuildGenix.Data.Repositories;

namespace BuildGenix.WebApi.Tests.Fakes;

public sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly Dictionary<string, ProjectEntity> _items = new(StringComparer.Ordinal);

    public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ProjectEntity?> GetAsync(string projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.TryGetValue(projectId, out var p) ? p : null);

    public Task<IReadOnlyList<ProjectEntity>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectEntity>>(_items.Values.OrderBy(p => p.Name).ToList());

    public Task UpsertAsync(ProjectEntity project, CancellationToken cancellationToken = default)
    {
        _items[project.RowKey] = project;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory stand-in for D-C4/C4b/C5 lifecycle rules without Azurite.</summary>
public sealed class InMemoryWorkItemRepository : IWorkItemRepository
{
    private readonly Dictionary<string, WorkItemEntity> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeByProject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    public Task EnsureTablesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool HasActiveLock(string projectId) => _activeByProject.ContainsKey(projectId);

    public Task<WorkItemEntity?> GetByIdAsync(string workItemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(workItemId, out var item) ? Clone(item) : null);

    public Task<IReadOnlyList<WorkItemEntity>> ListByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkItemEntity>>(
            _byId.Values.Where(i => i.ProjectId == projectId).Select(Clone).ToList());

    public Task<WorkItemCreateResult> TryCreateAsync(
        string projectId,
        string projectName,
        string userId,
        string? idempotencyKey,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        var next = _counters.GetValueOrDefault(projectId, 0) + 1;
        _counters[projectId] = next;
        var id = Guid.CreateVersion7().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var item = new WorkItemEntity
        {
            PartitionKey = projectId,
            RowKey = id,
            WorkItemId = id,
            ProjectId = projectId,
            ProjectName = projectName,
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Status = WorkItemStatuses.Queued,
            DisplayNumber = next,
            CreatedAt = now,
            UpdatedAt = now,
            BlobPrefix = $"{projectId}/{id}",
            LatestCommittedSummaryRevision = 0
        };
        _byId[id] = item;
        return Task.FromResult(new WorkItemCreateResult(WorkItemCreateOutcome.Created, Clone(item)));
    }

    public Task<WorkItemImplementResult> TryImplementAsync(
        string workItemId,
        int summaryRevision,
        CancellationToken cancellationToken = default)
    {
        if (summaryRevision <= 0)
        {
            return Task.FromResult(new WorkItemImplementResult(WorkItemImplementOutcome.InvalidRevision, null));
        }

        if (!_byId.TryGetValue(workItemId, out var item))
        {
            return Task.FromResult(new WorkItemImplementResult(WorkItemImplementOutcome.NotFound, null));
        }

        if (item.PipelineStarted && item.BoundSummaryRevision == summaryRevision)
        {
            return Task.FromResult(new WorkItemImplementResult(WorkItemImplementOutcome.AlreadyStarted, Clone(item)));
        }

        if (item.PipelineStarted
            && item.BoundSummaryRevision != summaryRevision
            && !WorkItemStatuses.IsTerminal(item.Status))
        {
            return Task.FromResult(new WorkItemImplementResult(WorkItemImplementOutcome.SameWiDifferentRevisionOpen, Clone(item)));
        }

        if (summaryRevision > item.LatestCommittedSummaryRevision)
        {
            return Task.FromResult(new WorkItemImplementResult(WorkItemImplementOutcome.SummaryNotFound, Clone(item)));
        }

        if (_activeByProject.TryGetValue(item.ProjectId, out var holder) && holder != item.WorkItemId)
        {
            var blocking = _byId[holder];
            return Task.FromResult(new WorkItemImplementResult(
                WorkItemImplementOutcome.ActiveConflict,
                Clone(item),
                holder,
                blocking.DisplayNumber));
        }

        _activeByProject[item.ProjectId] = item.WorkItemId;
        item.PipelineStarted = true;
        item.BoundSummaryRevision = summaryRevision;
        item.Status = WorkItemStatuses.Queued;
        item.CurrentStage = "PRD";
        item.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(new WorkItemImplementResult(WorkItemImplementOutcome.Started, Clone(item)));
    }

    public Task<IntakeSummaryCommitResult?> TryCommitIntakeSummaryAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        if (!_byId.TryGetValue(workItemId, out var item))
        {
            return Task.FromResult<IntakeSummaryCommitResult?>(null);
        }

        var revision = item.LatestCommittedSummaryRevision + 1;
        var now = DateTimeOffset.UtcNow;
        item.LatestCommittedSummaryRevision = revision;
        item.UpdatedAt = now;
        var artifactId = Guid.CreateVersion7().ToString("N");
        var blobPath = $"{item.ProjectId}/{item.WorkItemId}/v{revision}/intake-summary.json";
        return Task.FromResult<IntakeSummaryCommitResult?>(
            new IntakeSummaryCommitResult(true, revision, artifactId, blobPath, now));
    }

    public Task<IntakeSummaryCommitResult?> GetIntakeSummaryAsync(
        string workItemId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        if (revision <= 0 || !_byId.TryGetValue(workItemId, out var item))
        {
            return Task.FromResult<IntakeSummaryCommitResult?>(null);
        }

        if (revision > item.LatestCommittedSummaryRevision)
        {
            return Task.FromResult<IntakeSummaryCommitResult?>(
                new IntakeSummaryCommitResult(true, 0, string.Empty, string.Empty, default));
        }

        var artifactId = $"intake-summary-r{revision:D4}";
        var blobPath = $"{item.ProjectId}/{item.WorkItemId}/v{revision}/intake-summary.json";
        return Task.FromResult<IntakeSummaryCommitResult?>(
            new IntakeSummaryCommitResult(true, revision, artifactId, blobPath, item.UpdatedAt));
    }

    public Task<bool> TryMarkTerminalAsync(
        string workItemId,
        string terminalStatus,
        CancellationToken cancellationToken = default)
    {
        if (!WorkItemStatuses.ReleasesActiveMarker(terminalStatus)
            || !_byId.TryGetValue(workItemId, out var item))
        {
            return Task.FromResult(false);
        }

        item.Status = terminalStatus;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        if (_activeByProject.TryGetValue(item.ProjectId, out var holder)
            && holder == item.WorkItemId)
        {
            _activeByProject.Remove(item.ProjectId);
        }

        return Task.FromResult(true);
    }

    private static WorkItemEntity Clone(WorkItemEntity e) => new()
    {
        PartitionKey = e.PartitionKey,
        RowKey = e.RowKey,
        WorkItemId = e.WorkItemId,
        ProjectId = e.ProjectId,
        ProjectName = e.ProjectName,
        UserId = e.UserId,
        Title = e.Title,
        Status = e.Status,
        CurrentStage = e.CurrentStage,
        DisplayNumber = e.DisplayNumber,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        BlobPrefix = e.BlobPrefix,
        BoundSummaryRevision = e.BoundSummaryRevision,
        LatestCommittedSummaryRevision = e.LatestCommittedSummaryRevision,
        PipelineStarted = e.PipelineStarted,
        HumanMessage = e.HumanMessage,
        CurrentInputVersion = e.CurrentInputVersion
    };
}
