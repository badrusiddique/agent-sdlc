using Azure;
using Azure.Data.Tables;

namespace BuildGenix.Data.Entities;

public sealed class WorkItemEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // workItemId
    public string RowKey { get; set; } = string.Empty; // ULID / sequence
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string WorkItemId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
