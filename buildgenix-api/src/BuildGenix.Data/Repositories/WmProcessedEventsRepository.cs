using Azure;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BuildGenix.Data.Repositories;

public sealed class WmProcessedEventsRepository : IWmProcessedEventsRepository
{
    private readonly ITableEntityWriter _writer;
    private readonly ILogger<WmProcessedEventsRepository> _logger;

    public WmProcessedEventsRepository(
        ITableEntityWriter writer,
        ILogger<WmProcessedEventsRepository> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        await _writer.EnsureTableAsync(TableNames.WmProcessedEvents, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Ensured table {Table}", TableNames.WmProcessedEvents);
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
            ServiceBusMessageId = messageId,
            EnqueuedAt = enqueuedAtUtc,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _writer
                .AddEntityAsync(TableNames.WmProcessedEvents, entity, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogDebug(
                "Marked WM message {MessageId} from {Source} in {Partition}",
                messageId,
                source,
                entity.PartitionKey);
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
