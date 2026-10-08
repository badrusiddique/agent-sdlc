using Azure;
using Azure.Data.Tables;

namespace BuildGenix.Data.Entities;

public sealed class ApprovalEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // workItemId
    public string RowKey { get; set; } = string.Empty; // gateId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string WorkItemId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Decision { get; set; } = "Approved";
    public string UserId { get; set; } = "dummy-user";
    public int InputVersion { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}
