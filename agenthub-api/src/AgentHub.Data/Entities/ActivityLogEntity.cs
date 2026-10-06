using Azure;
using Azure.Data.Tables;

namespace AgentHub.Data.Entities;

/// <summary>
/// Agent activity trace. PK = {workItemId}_{yyyyMM}, RK = inverted-ticks_guid.
/// </summary>
public sealed class ActivityLogEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string WorkItemId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public string? DetailJson { get; set; }
    public string? CorrelationId { get; set; }

    public static string PartitionKeyFor(string workItemId, DateTimeOffset utc) =>
        $"{workItemId}_{utc.UtcDateTime:yyyyMM}";

    public static string RowKeyFor(DateTimeOffset utc, Guid id) =>
        $"{(DateTimeOffset.MaxValue.UtcTicks - utc.UtcTicks).ToString("D20")}_{id:N}";
}
