namespace AgentHub.Data;

/// <summary>
/// AgentHub-owned Azure Table names only. BuildGenix tables must never appear here (D-CL-7 / D-AS-4).
/// </summary>
public static class TableCatalog
{
    public static readonly IReadOnlyList<string> Built =
    [
        TableNames.ActivityLog,
        TableNames.EffectLedger,
        TableNames.AgProcessedEvents
    ];
}

public static class TableNames
{
    public const string ActivityLog = "ActivityLog";
    public const string EffectLedger = "EffectLedger";
    public const string AgProcessedEvents = "AgProcessedEvents";
}
