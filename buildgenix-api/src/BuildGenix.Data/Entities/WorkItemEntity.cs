using Azure;
using Azure.Data.Tables;

namespace BuildGenix.Data.Entities;

public sealed class WorkItemEntity : ITableEntity
{
    public const string ActiveRowKey = "~ACTIVE";
    public const string CounterRowKey = "~COUNTER";
    public const string ClaimPrefix = "~CLAIM_";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string WorkItemId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string Status { get; set; } = WorkItemStatuses.Queued;
    public int DisplayNumber { get; set; }
    public string? LastError { get; set; }
    public string? CurrentTaskId { get; set; }
    public string? BlobPrefix { get; set; }
    public string? HumanMessage { get; set; }
    public int CurrentInputVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Intake summary revision bound at Implement now (D-C5).</summary>
    public int BoundSummaryRevision { get; set; }

    /// <summary>
    /// Highest committed intake summary revision for this WI (marker until full B1 Conversations).
    /// Implement requires summaryRevision &lt;= this value and &gt; 0.
    /// </summary>
    public int LatestCommittedSummaryRevision { get; set; }

    /// <summary>True after Implement starts the pipeline for BoundSummaryRevision.</summary>
    public bool PipelineStarted { get; set; }

    public DateTimeOffset? AcquiredAt { get; set; }
    public int NextNumber { get; set; }
}

public static class WorkItemStatuses
{
    public const string Queued = "Queued";
    public const string AwaitingApproval = "AwaitingApproval";
    public const string HumanActionRequired = "HumanActionRequired";
    public const string Failed = "Failed";
    public const string Completed = "Completed";

    public static bool IsTerminal(string status) =>
        status is Failed or Completed;

    public static bool ReleasesActiveMarker(string status) => IsTerminal(status);
}

public sealed class WorkItemIndexEntity : ITableEntity
{
    public const string RootRowKey = "ROOT";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = RootRowKey;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ProjectId { get; set; } = string.Empty;
}
