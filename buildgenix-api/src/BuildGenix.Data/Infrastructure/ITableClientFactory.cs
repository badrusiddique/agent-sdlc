using Azure.Data.Tables;

namespace BuildGenix.Data.Infrastructure;

public interface ITableClientFactory
{
    TableClient GetClient(string logicalTableName);

    string ResolveTableName(string logicalTableName);
}
