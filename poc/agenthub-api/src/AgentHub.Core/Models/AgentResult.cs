namespace AgentHub.Core.Models;

public sealed class AgentResult
{
    public string WorkItemId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = AgentResultStatus.Completed;
    public string CorrelationId { get; set; } = string.Empty;
    public int InputVersion { get; set; }
    public string RunId { get; set; } = string.Empty;
    public Dictionary<string, string> Artifacts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.Ordinal);
    public long DurationMs { get; set; }
    public TokenUsage? TokenUsage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? HumanMessage { get; set; }
    public string? StateContextId { get; set; }
    public int OutputRevision { get; set; } = 1;
}

public static class AgentResultStatus
{
    public const string Completed = "Completed";
    public const string HumanActionRequired = "HumanActionRequired";
    public const string Failed = "Failed";
}

public sealed class TokenUsage
{
    public int Input { get; set; }
    public int Output { get; set; }
    public string? ModelId { get; set; }
}
