using Azure.Data.Tables;

namespace BuildGenix.Data.Infrastructure;

/// <summary>
/// Narrow table write surface so Add/409 semantics can be unit-tested without Azurite.
/// </summary>
public interface ITableEntityWriter
{
    Task EnsureTableAsync(string logicalTableName, CancellationToken cancellationToken = default);

    Task AddEntityAsync<T>(string logicalTableName, T entity, CancellationToken cancellationToken = default)
        where T : ITableEntity;
}
