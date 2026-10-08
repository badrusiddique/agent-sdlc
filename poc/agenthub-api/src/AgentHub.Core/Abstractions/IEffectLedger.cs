namespace AgentHub.Core.Abstractions;

/// <summary>Host-neutral EffectLedger port (D-C2). Implementations live in Infrastructure over AgentHub.Data.</summary>
public interface IEffectLedger
{
    Task<CommittedEffect?> TryGetCommittedAsync(
        string workItemId,
        string taskId,
        string intentKind,
        int ordinal,
        CancellationToken cancellationToken = default);

    Task<EffectCommitResult> CommitAsync(
        EffectIntent intent,
        CancellationToken cancellationToken = default);
}

public sealed record EffectIntent(
    string WorkItemId,
    string TaskId,
    string IntentKind,
    int Ordinal,
    string? InputJson,
    string? OutputJson);

public sealed record CommittedEffect(string? InputJson, string? OutputJson);

public sealed record EffectCommitResult(bool WasFirstWriter, CommittedEffect Effect);
