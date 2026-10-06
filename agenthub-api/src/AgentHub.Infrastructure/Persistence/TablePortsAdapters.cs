using AgentHub.Core.Abstractions;
using AgentHub.Data.Entities;
using AgentHub.Data.Repositories;

namespace AgentHub.Infrastructure.Persistence;

public sealed class EffectLedgerAdapter : IEffectLedger
{
    private readonly IEffectLedgerRepository _inner;

    public EffectLedgerAdapter(IEffectLedgerRepository inner) => _inner = inner;

    public async Task<CommittedEffect?> TryGetCommittedAsync(
        string workItemId,
        string taskId,
        string intentKind,
        int ordinal,
        CancellationToken cancellationToken = default)
    {
        var row = await _inner
            .TryGetCommittedAsync(workItemId, taskId, intentKind, ordinal, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : new CommittedEffect(row.InputJson, row.OutputJson);
    }

    public async Task<EffectCommitResult> CommitAsync(
        EffectIntent intent,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .CommitAsync(
                new EffectLedgerEntity
                {
                    WorkItemId = intent.WorkItemId,
                    TaskId = intent.TaskId,
                    IntentKind = intent.IntentKind,
                    Ordinal = intent.Ordinal,
                    InputJson = intent.InputJson,
                    OutputJson = intent.OutputJson
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new EffectCommitResult(
            result.WasFirstWriter,
            new CommittedEffect(result.Entity.InputJson, result.Entity.OutputJson));
    }
}

public sealed class ActivityLogAdapter : IActivityLog
{
    private readonly IActivityLogRepository _inner;

    public ActivityLogAdapter(IActivityLogRepository inner) => _inner = inner;

    public Task AppendAsync(
        string workItemId,
        string taskId,
        string stage,
        string level,
        string message,
        string? correlationId,
        CancellationToken cancellationToken = default) =>
        _inner.AppendAsync(
            new ActivityLogEntity
            {
                WorkItemId = workItemId,
                TaskId = taskId,
                Stage = stage,
                Level = level,
                Message = message,
                CorrelationId = correlationId
            },
            cancellationToken);
}

public sealed class MessageDedupAdapter : IMessageDedup
{
    private readonly IAgProcessedEventsRepository _inner;

    public MessageDedupAdapter(IAgProcessedEventsRepository inner) => _inner = inner;

    public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
        _inner.IsProcessedAsync(messageId, cancellationToken);

    public Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
        _inner.TryMarkProcessedAsync(messageId, cancellationToken);
}
