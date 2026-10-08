using Azure.Data.Tables;

namespace AgentHub.Data.Infrastructure;

public interface ITableClientFactory
{
    TableClient GetClient(string logicalTableName);

    string ResolveTableName(string logicalTableName);
}
