namespace AgentHub.Data.Repositories;

public interface IAgProcessedEventsRepository
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);

    Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to mark a message as processed. Returns false if already present (duplicate).
    /// </summary>
    Task<bool> TryMarkProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default);
}
