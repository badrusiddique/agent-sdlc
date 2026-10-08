using Azure;
using Azure.Data.Tables;

namespace AgentHub.Data.Entities;

/// <summary>
/// AgentHub message dedup. PK = AGPROC (constant), RK = serviceBusMessageId (D-C1).
/// </summary>
public sealed class AgProcessedEventEntity : ITableEntity
{
    public const string FixedPartitionKey = "AGPROC";

    public string PartitionKey { get; set; } = FixedPartitionKey;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string MessageId { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
