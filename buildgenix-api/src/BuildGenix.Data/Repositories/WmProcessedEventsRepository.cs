using Azure;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BuildGenix.Data.Repositories;

public sealed class WmProcessedEventsRepository : IWmProcessedEventsRepository
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<WmProcessedEventsRepository> _logger;

    public WmProcessedEventsRepository(
        ITableClientFactory factory,
        ILogger<WmProcessedEventsRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(TableNames.WmProcessedEvents);
        await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Ensured table {Table}", _factory.ResolveTableName(TableNames.WmProcessedEvents));
    }

    public async Task<bool> TryMarkProcessedAsync(
        string messageId,
        DateTimeOffset enqueuedAtUtc,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var entity = new WmProcessedEventEntity
        {
            PartitionKey = WmProcessedEventEntity.PartitionKeyFor(enqueuedAtUtc),
            RowKey = messageId,
            ProcessedAt = DateTimeOffset.UtcNow,
            Source = source
        };

        var client = _factory.GetClient(TableNames.WmProcessedEvents);
        try
        {
            await client.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogInformation(
                "Duplicate WM message {MessageId} already marked in {Partition}",
                messageId,
                entity.PartitionKey);
            return false;
        }
    }
}
