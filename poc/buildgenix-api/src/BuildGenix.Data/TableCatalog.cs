namespace BuildGenix.Data;

/// <summary>
/// BuildGenix-owned Azure Table names only. AgentHub tables must never appear here (D-CL-7).
/// </summary>
public static class TableCatalog
{
    public static readonly IReadOnlyList<string> Built =
    [
        TableNames.Projects,
        TableNames.WorkItems,
        TableNames.WorkItemIndex,
        TableNames.WorkItemsByCreator,
        TableNames.Artifacts,
        TableNames.ApprovalGates,
        TableNames.Approvals,
        TableNames.WorkItemEvents,
        TableNames.SgProcessedEvents,
        TableNames.WmProcessedEvents
    ];
}

public static class TableNames
{
    public const string Projects = "Projects";
    public const string WorkItems = "WorkItems";
    public const string WorkItemIndex = "WorkItemIndex";
    public const string WorkItemsByCreator = "WorkItemsByCreator";
    public const string Artifacts = "Artifacts";
    public const string ApprovalGates = "ApprovalGates";
    public const string Approvals = "Approvals";
    public const string WorkItemEvents = "WorkItemEvents";
    public const string SgProcessedEvents = "SgProcessedEvents";
    public const string WmProcessedEvents = "WmProcessedEvents";
}
