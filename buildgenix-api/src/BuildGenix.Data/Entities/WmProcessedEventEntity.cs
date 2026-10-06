using Azure;
using Azure.Data.Tables;

namespace BuildGenix.Data.Entities;

/// <summary>
/// Heartbeat (and future WM) idempotency marker. PK = WMPROC_{yyyyMMdd}, RK = messageId.
/// Columns aligned with db-schema entity 20.
/// </summary>
public sealed class WmProcessedEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ServiceBusMessageId { get; set; } = string.Empty;
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }

    public static string PartitionKeyFor(DateTimeOffset utc) =>
        $"WMPROC_{utc.UtcDateTime:yyyyMMdd}";
}
