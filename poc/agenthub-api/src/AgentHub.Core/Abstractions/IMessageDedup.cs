namespace AgentHub.Core.Abstractions;

/// <summary>Host-neutral AgProcessedEvents dedup port (D-C1).</summary>
public interface IMessageDedup
{
    Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken cancellationToken = default);
}
