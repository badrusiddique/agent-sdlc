using Azure;
using AgentHub.Data.Entities;
using AgentHub.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AgentHub.Data.Repositories;

public sealed class EffectLedgerRepository : IEffectLedgerRepository
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<EffectLedgerRepository> _logger;

    public EffectLedgerRepository(
        ITableClientFactory factory,
        ILogger<EffectLedgerRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(TableNames.EffectLedger);
        await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Ensured table {Table}", _factory.ResolveTableName(TableNames.EffectLedger));
    }

    public async Task<EffectLedgerCommitResult> CommitAsync(
        EffectLedgerEntity entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.WorkItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.TaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.IntentKind);

        entry.PartitionKey = entry.WorkItemId;
        entry.RowKey = EffectLedgerEntity.RowKeyFor(entry.TaskId, entry.IntentKind, entry.Ordinal);
        entry.Status = EffectLedgerEntity.CommittedStatus;
        entry.ErrorMessage = null;

        var client = _factory.GetClient(TableNames.EffectLedger);
        try
        {
            await client.AddEntityAsync(entry, cancellationToken).ConfigureAwait(false);
            return new EffectLedgerCommitResult(WasFirstWriter: true, entry);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var existing = await TryGetCommittedAsync(
                    entry.WorkItemId,
                    entry.TaskId,
                    entry.IntentKind,
                    entry.Ordinal,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                throw;
            }

            _logger.LogInformation(
                "EffectLedger first-writer-wins for {RowKey}; returning stored output",
                entry.RowKey);
            return new EffectLedgerCommitResult(WasFirstWriter: false, existing);
        }
    }

    public async Task<EffectLedgerEntity?> TryGetCommittedAsync(
        string workItemId,
        string taskId,
        string intentKind,
        int ordinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        var client = _factory.GetClient(TableNames.EffectLedger);
        var rowKey = EffectLedgerEntity.RowKeyFor(taskId, intentKind, ordinal);
        try
        {
            var response = await client
                .GetEntityAsync<EffectLedgerEntity>(workItemId, rowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
