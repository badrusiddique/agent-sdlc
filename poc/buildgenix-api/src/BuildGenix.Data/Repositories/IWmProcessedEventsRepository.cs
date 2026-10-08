namespace BuildGenix.Data.Repositories;

public interface IWmProcessedEventsRepository
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to mark a message as processed. Returns false if already present (duplicate).
    /// </summary>
    Task<bool> TryMarkProcessedAsync(
        string messageId,
        DateTimeOffset enqueuedAtUtc,
        string source,
        CancellationToken cancellationToken = default);
}
