using Azure.Data.Tables;

namespace BuildGenix.Data.Infrastructure;

public sealed class TableEntityWriter : ITableEntityWriter
{
    private readonly ITableClientFactory _factory;

    public TableEntityWriter(ITableClientFactory factory) => _factory = factory;

    public async Task EnsureTableAsync(string logicalTableName, CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(logicalTableName);
        await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddEntityAsync<T>(string logicalTableName, T entity, CancellationToken cancellationToken = default)
        where T : ITableEntity
    {
        var client = _factory.GetClient(logicalTableName);
        await client.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
    }
}
