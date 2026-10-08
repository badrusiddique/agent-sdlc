using Azure;
using AgentHub.Data.Entities;
using AgentHub.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AgentHub.Data.Repositories;

public sealed class AgProcessedEventsRepository : IAgProcessedEventsRepository
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<AgProcessedEventsRepository> _logger;

    public AgProcessedEventsRepository(
        ITableClientFactory factory,
        ILogger<AgProcessedEventsRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(TableNames.AgProcessedEvents);
        await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Ensured table {Table}", _factory.ResolveTableName(TableNames.AgProcessedEvents));
    }

    public async Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var client = _factory.GetClient(TableNames.AgProcessedEvents);
        try
        {
            await client
                .GetEntityAsync<AgProcessedEventEntity>(
                    AgProcessedEventEntity.FixedPartitionKey,
                    messageId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> TryMarkProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var entity = new AgProcessedEventEntity
        {
            PartitionKey = AgProcessedEventEntity.FixedPartitionKey,
            RowKey = messageId,
            MessageId = messageId,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        var client = _factory.GetClient(TableNames.AgProcessedEvents);
        try
        {
            await client.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogInformation("Duplicate AG message {MessageId} already marked", messageId);
            return false;
        }
    }
}
