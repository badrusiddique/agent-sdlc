using Azure;
using Azure.Data.Tables;

namespace AgentHub.Data.Entities;

/// <summary>
/// Write-once effect ledger. PK = workItemId, RK = {taskId}_{intentKind}_{ordinal:D6}.
/// Status is always Committed (D-C2). No Replace; HTTP 409 = first writer wins.
/// </summary>
public sealed class EffectLedgerEntity : ITableEntity
{
    public const string CommittedStatus = "Committed";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string WorkItemId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string IntentKind { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Status { get; set; } = CommittedStatus;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }

    public static string RowKeyFor(string taskId, string intentKind, int ordinal) =>
        $"{taskId}_{intentKind}_{ordinal:D6}";
}
