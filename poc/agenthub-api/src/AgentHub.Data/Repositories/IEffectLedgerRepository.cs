using AgentHub.Data.Entities;

namespace AgentHub.Data.Repositories;

public interface IEffectLedgerRepository
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Write-once commit. Returns stored output (existing row on 409, or newly written).
    /// </summary>
    Task<EffectLedgerCommitResult> CommitAsync(
        EffectLedgerEntity entry,
        CancellationToken cancellationToken = default);

    Task<EffectLedgerEntity?> TryGetCommittedAsync(
        string workItemId,
        string taskId,
        string intentKind,
        int ordinal,
        CancellationToken cancellationToken = default);
}

public sealed record EffectLedgerCommitResult(
    bool WasFirstWriter,
    EffectLedgerEntity Entity);
