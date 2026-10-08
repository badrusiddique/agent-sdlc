using System.Collections.Concurrent;
using System.Text.Json;
using AgentHub.Core.Abstractions;

namespace AgentHub.Infrastructure.Persistence;

public sealed class InMemoryEffectLedger : IEffectLedger
{
    private readonly ConcurrentDictionary<string, CommittedEffect> _rows = new(StringComparer.Ordinal);

    public Task<CommittedEffect?> TryGetCommittedAsync(
        string workItemId,
        string taskId,
        string intentKind,
        int ordinal,
        CancellationToken cancellationToken = default)
    {
        var key = $"{taskId}_{intentKind}_{ordinal:D6}";
        return Task.FromResult(_rows.TryGetValue(key, out var e) ? e : null);
    }

    public Task<EffectCommitResult> CommitAsync(
        EffectIntent intent,
        CancellationToken cancellationToken = default)
    {
        var key = $"{intent.TaskId}_{intent.IntentKind}_{intent.Ordinal:D6}";
        if (_rows.TryGetValue(key, out var existing))
        {
            return Task.FromResult(new EffectCommitResult(false, existing));
        }

        var effect = new CommittedEffect(intent.InputJson, intent.OutputJson);
        _rows[key] = effect;
        return Task.FromResult(new EffectCommitResult(true, effect));
    }
}

public sealed class InMemoryActivityLog : IActivityLog
{
    public ConcurrentBag<(string WorkItemId, string Message)> Entries { get; } = [];

    public Task AppendAsync(
        string workItemId,
        string taskId,
        string stage,
        string level,
        string message,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        Entries.Add((workItemId, message));
        return Task.CompletedTask;
    }
}

public sealed class InMemoryMessageDedup : IMessageDedup
{
    private readonly ConcurrentDictionary<string, byte> _ids = new(StringComparer.Ordinal);

    public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ids.ContainsKey(messageId));

    public Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ids.TryAdd(messageId, 0));
}
