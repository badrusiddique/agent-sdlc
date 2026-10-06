using Azure.Data.Tables;
using Azure.Identity;
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
        _serviceClient = CreateServiceClient(_options);
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

    private static TableServiceClient CreateServiceClient(TableStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new TableServiceClient(options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUri))
        {
            return new TableServiceClient(new Uri(options.ServiceUri), new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "TableStorage:ConnectionString (local/Azurite) or TableStorage:ServiceUri (DefaultAzureCredential) is required.");
    }
}
