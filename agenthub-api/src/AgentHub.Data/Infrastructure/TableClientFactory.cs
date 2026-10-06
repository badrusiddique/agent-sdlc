using Azure.Data.Tables;
using AgentHub.Data.Options;
using Microsoft.Extensions.Options;

namespace AgentHub.Data.Infrastructure;

public sealed class TableClientFactory : ITableClientFactory
{
    private readonly TableServiceClient _serviceClient;
    private readonly TableStorageOptions _options;

    public TableClientFactory(IOptions<TableStorageOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "TableStorage:ConnectionString is required. Use the Azurite connection string for local Development.");
        }

        _serviceClient = new TableServiceClient(_options.ConnectionString);
    }

    public TableClient GetClient(string logicalTableName) =>
        _serviceClient.GetTableClient(ResolveTableName(logicalTableName));

    public string ResolveTableName(string logicalTableName)
    {
        if (_options.Tables.TryGetValue(logicalTableName, out var overrideName)
            && !string.IsNullOrWhiteSpace(overrideName))
        {
            return overrideName;
        }

        return string.IsNullOrEmpty(_options.StoragePrefix)
            ? logicalTableName
            : $"{_options.StoragePrefix}{logicalTableName}";
    }
}
