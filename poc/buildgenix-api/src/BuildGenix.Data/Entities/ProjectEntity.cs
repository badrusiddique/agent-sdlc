using Azure;
using Azure.Data.Tables;

namespace BuildGenix.Data.Entities;

public sealed class ProjectEntity : ITableEntity
{
    public const string DefaultPartitionKey = "default";

    public string PartitionKey { get; set; } = DefaultPartitionKey;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
