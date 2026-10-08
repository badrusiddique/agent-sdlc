using Azure;
using Azure.Data.Tables;
using BuildGenix.Data;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using BuildGenix.Data.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuildGenix.WebApi.Tests;

public sealed class WmProcessedEventsRepositoryTests
{
    [Fact]
    public async Task TryMarkProcessedAsync_returns_true_on_first_insert_and_false_on_409()
    {
        var writer = new InMemoryTableEntityWriter();
        var repo = new WmProcessedEventsRepository(writer, NullLogger<WmProcessedEventsRepository>.Instance);
        var enqueued = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.True(await repo.TryMarkProcessedAsync("msg-1", enqueued, "bg-heartbeat-ping"));
        Assert.False(await repo.TryMarkProcessedAsync("msg-1", enqueued, "bg-heartbeat-ping"));

        var stored = Assert.Single(writer.Entities);
        Assert.Equal("WMPROC_20261006", stored.PartitionKey);
        Assert.Equal("msg-1", stored.RowKey);
        Assert.Equal("msg-1", stored.ServiceBusMessageId);
        Assert.Equal(enqueued, stored.EnqueuedAt);
    }

    private sealed class InMemoryTableEntityWriter : ITableEntityWriter
    {
        public List<WmProcessedEventEntity> Entities { get; } = [];

        public Task EnsureTableAsync(string logicalTableName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddEntityAsync<T>(string logicalTableName, T entity, CancellationToken cancellationToken = default)
            where T : ITableEntity
        {
            Assert.Equal(TableNames.WmProcessedEvents, logicalTableName);
            var wm = Assert.IsType<WmProcessedEventEntity>(entity);
            var key = $"{wm.PartitionKey}|{wm.RowKey}";
            if (Entities.Any(e => $"{e.PartitionKey}|{e.RowKey}" == key))
            {
                throw new RequestFailedException(409, "EntityAlreadyExists");
            }

            Entities.Add(wm);
            return Task.CompletedTask;
        }
    }
}
