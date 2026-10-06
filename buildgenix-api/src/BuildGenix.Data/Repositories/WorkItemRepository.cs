using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BuildGenix.Data.Repositories;

public sealed class WorkItemRepository : IWorkItemRepository
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<WorkItemRepository> _logger;

    public WorkItemRepository(ITableClientFactory factory, ILogger<WorkItemRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        await _factory.GetClient(TableNames.WorkItems).CreateIfNotExistsAsync(cancellationToken)
            .ConfigureAwait(false);
        await _factory.GetClient(TableNames.WorkItemIndex).CreateIfNotExistsAsync(cancellationToken)
            .ConfigureAwait(false);
        await _factory.GetClient(TableNames.WorkItemsByCreator).CreateIfNotExistsAsync(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug("Ensured WorkItems tables");
    }

    public async Task<WorkItemEntity?> GetByIdAsync(string workItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        var indexClient = _factory.GetClient(TableNames.WorkItemIndex);
        try
        {
            var index = await indexClient
                .GetEntityAsync<WorkItemIndexEntity>(workItemId, WorkItemIndexEntity.RootRowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await GetInProjectAsync(index.Value.ProjectId, workItemId, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<WorkItemEntity>> ListByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var client = _factory.GetClient(TableNames.WorkItems);
        var items = new List<WorkItemEntity>();
        await foreach (var entity in client.QueryAsync<WorkItemEntity>(
                           e => e.PartitionKey == projectId,
                           cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (entity.RowKey.StartsWith("~", StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(entity);
        }

        return items.OrderByDescending(i => i.DisplayNumber).ToList();
    }

    public async Task<WorkItemCreateResult> TryCreateAsync(
        string projectId,
        string projectName,
        string userId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await FindByClaimAsync(projectId, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return new WorkItemCreateResult(WorkItemCreateOutcome.Duplicate, existing);
            }
        }

        var workItemId = Guid.CreateVersion7().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var client = _factory.GetClient(TableNames.WorkItems);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var nextNumber = await ReadNextDisplayNumberAsync(client, projectId, cancellationToken).ConfigureAwait(false);
            var item = new WorkItemEntity
            {
                PartitionKey = projectId,
                RowKey = workItemId,
                WorkItemId = workItemId,
                ProjectId = projectId,
                ProjectName = projectName,
                UserId = userId,
                Status = WorkItemStatuses.Queued,
                CurrentStage = string.Empty,
                DisplayNumber = nextNumber,
                CurrentInputVersion = 0,
                BlobPrefix = $"{projectId}/{workItemId}",
                CreatedAt = now,
                UpdatedAt = now,
                PipelineStarted = false,
                BoundSummaryRevision = 0
            };

            var actions = new List<TableTransactionAction>
            {
                new(TableTransactionActionType.UpsertReplace, BuildCounter(projectId, nextNumber + 1)),
                new(TableTransactionActionType.Add, item)
            };

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                actions.Insert(0, new TableTransactionAction(
                    TableTransactionActionType.Add,
                    BuildClaim(projectId, idempotencyKey, workItemId, now)));
            }

            try
            {
                await client.SubmitTransactionAsync(actions, cancellationToken).ConfigureAwait(false);
            }
            catch (TableTransactionFailedException ex) when (ex.FailedTransactionActionIndex is not null)
            {
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    var dup = await FindByClaimAsync(projectId, idempotencyKey, cancellationToken).ConfigureAwait(false);
                    if (dup is not null)
                    {
                        return new WorkItemCreateResult(WorkItemCreateOutcome.Duplicate, dup);
                    }
                }

                _logger.LogDebug(ex, "Work item create retry {Attempt} for project {ProjectId}", attempt, projectId);
                continue;
            }

            await WriteLookupsAsync(item, cancellationToken).ConfigureAwait(false);
            // D-C4b: create does NOT acquire ~ACTIVE.
            return new WorkItemCreateResult(WorkItemCreateOutcome.Created, item);
        }

        throw new InvalidOperationException($"Failed to create work item in project {projectId} after retries.");
    }

    public async Task<WorkItemImplementResult> TryImplementAsync(
        string workItemId,
        int summaryRevision,
        CancellationToken cancellationToken = default)
    {
        if (summaryRevision <= 0)
        {
            return new WorkItemImplementResult(WorkItemImplementOutcome.InvalidRevision, null);
        }

        var item = await GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return new WorkItemImplementResult(WorkItemImplementOutcome.NotFound, null);
        }

        // D-C5: same revision already started => success no-op.
        if (item.PipelineStarted && item.BoundSummaryRevision == summaryRevision)
        {
            return new WorkItemImplementResult(WorkItemImplementOutcome.AlreadyStarted, item);
        }

        // I1-R1 OPEN: same WI, different revision, pipeline non-terminal.
        if (item.PipelineStarted
            && item.BoundSummaryRevision != summaryRevision
            && !WorkItemStatuses.IsTerminal(item.Status))
        {
            return new WorkItemImplementResult(WorkItemImplementOutcome.SameWiDifferentRevisionOpen, item);
        }

        var client = _factory.GetClient(TableNames.WorkItems);
        var now = DateTimeOffset.UtcNow;

        try
        {
            var active = new WorkItemEntity
            {
                PartitionKey = item.ProjectId,
                RowKey = WorkItemEntity.ActiveRowKey,
                WorkItemId = item.WorkItemId,
                ProjectId = item.ProjectId,
                AcquiredAt = now
            };
            await client.AddEntityAsync(active, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var holder = await GetActiveHolderAsync(client, item.ProjectId, cancellationToken).ConfigureAwait(false);
            if (holder is not null && holder.WorkItemId == item.WorkItemId)
            {
                // Same WI already holds lock; fall through to bind revision if needed.
            }
            else if (holder is not null)
            {
                var blocking = await GetInProjectAsync(item.ProjectId, holder.WorkItemId, cancellationToken)
                    .ConfigureAwait(false);
                return new WorkItemImplementResult(
                    WorkItemImplementOutcome.ActiveConflict,
                    item,
                    holder.WorkItemId,
                    blocking?.DisplayNumber);
            }
            else
            {
                return new WorkItemImplementResult(WorkItemImplementOutcome.ActiveConflict, item);
            }
        }

        item.BoundSummaryRevision = summaryRevision;
        item.PipelineStarted = true;
        item.Status = WorkItemStatuses.Queued;
        item.CurrentStage = "PRD";
        item.UpdatedAt = now;
        item.CurrentInputVersion = Math.Max(item.CurrentInputVersion, 1);

        await client.UpdateEntityAsync(item, item.ETag, TableUpdateMode.Replace, cancellationToken)
            .ConfigureAwait(false);

        return new WorkItemImplementResult(WorkItemImplementOutcome.Started, item);
    }

    private async Task<WorkItemEntity?> GetInProjectAsync(
        string projectId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var client = _factory.GetClient(TableNames.WorkItems);
        try
        {
            var response = await client
                .GetEntityAsync<WorkItemEntity>(projectId, workItemId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task WriteLookupsAsync(WorkItemEntity item, CancellationToken cancellationToken)
    {
        var index = new WorkItemIndexEntity
        {
            PartitionKey = item.WorkItemId,
            RowKey = WorkItemIndexEntity.RootRowKey,
            ProjectId = item.ProjectId
        };
        await _factory.GetClient(TableNames.WorkItemIndex)
            .UpsertEntityAsync(index, TableUpdateMode.Replace, cancellationToken)
            .ConfigureAwait(false);

        var byCreator = new WorkItemIndexEntity
        {
            PartitionKey = item.UserId,
            RowKey = item.WorkItemId,
            ProjectId = item.ProjectId
        };
        await _factory.GetClient(TableNames.WorkItemsByCreator)
            .UpsertEntityAsync(byCreator, TableUpdateMode.Replace, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> ReadNextDisplayNumberAsync(
        TableClient client,
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var counter = await client
                .GetEntityAsync<WorkItemEntity>(projectId, WorkItemEntity.CounterRowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return counter.Value.NextNumber <= 0 ? 1 : counter.Value.NextNumber;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return 1;
        }
    }

    private static WorkItemEntity BuildCounter(string projectId, int nextNumber) => new()
    {
        PartitionKey = projectId,
        RowKey = WorkItemEntity.CounterRowKey,
        ProjectId = projectId,
        NextNumber = nextNumber
    };

    private static WorkItemEntity BuildClaim(
        string projectId,
        string idempotencyKey,
        string workItemId,
        DateTimeOffset createdAt) => new()
    {
        PartitionKey = projectId,
        RowKey = ClaimRowKey(idempotencyKey),
        ProjectId = projectId,
        WorkItemId = workItemId,
        CreatedAt = createdAt
    };

    private static string ClaimRowKey(string idempotencyKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))).ToLowerInvariant();
        return WorkItemEntity.ClaimPrefix + hash;
    }

    private async Task<WorkItemEntity?> FindByClaimAsync(
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var client = _factory.GetClient(TableNames.WorkItems);
        try
        {
            var claim = await client
                .GetEntityAsync<WorkItemEntity>(projectId, ClaimRowKey(idempotencyKey), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await GetInProjectAsync(projectId, claim.Value.WorkItemId, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task<WorkItemEntity?> GetActiveHolderAsync(
        TableClient client,
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var active = await client
                .GetEntityAsync<WorkItemEntity>(projectId, WorkItemEntity.ActiveRowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return active.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
