using AgentHub.Data.Entities;
using AgentHub.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AgentHub.Data.Repositories;

public sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<ActivityLogRepository> _logger;

    public ActivityLogRepository(
        ITableClientFactory factory,
        ILogger<ActivityLogRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(TableNames.ActivityLog);
        await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Ensured table {Table}", _factory.ResolveTableName(TableNames.ActivityLog));
    }

    public async Task AppendAsync(ActivityLogEntity entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.WorkItemId);

        var at = entry.Timestamp ?? DateTimeOffset.UtcNow;
        entry.PartitionKey = ActivityLogEntity.PartitionKeyFor(entry.WorkItemId, at);
        entry.RowKey = string.IsNullOrWhiteSpace(entry.RowKey)
            ? ActivityLogEntity.RowKeyFor(at, Guid.NewGuid())
            : entry.RowKey;

        var client = _factory.GetClient(TableNames.ActivityLog);
        await client.UpsertEntityAsync(entry, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
