using Azure;
using Azure.Data.Tables;

namespace BuildGenix.Data.Entities;

public sealed class ArtifactEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // workItemId
    public string RowKey { get; set; } = string.Empty; // registryKey or ~HEAD_key
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string WorkItemId { get; set; } = string.Empty;
    public string RegistryKey { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string BlobPath { get; set; } = string.Empty;
}
