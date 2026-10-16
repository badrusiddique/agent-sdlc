namespace AgentHub.Agents.Pipeline.Artifacts;

/// <summary>
/// Crash-safe artifact layout under {projectId}/{workItemId}/.
/// Per-run blobs live in v{n}/; named copies (PRD_V1.json) and LATEST.json
/// let any agent recover the newest revision after a host crash.
/// </summary>
public static class VersionedArtifactPaths
{
    public static string WorkItemRoot(string projectId, string workItemId) =>
        $"{projectId}/{workItemId}/";

    public static string RevisionPrefix(string projectId, string workItemId, int revision) =>
        $"{projectId}/{workItemId}/v{Math.Max(1, revision)}/";

    public static string NamedVersion(string projectId, string workItemId, string logicalName, int revision) =>
        $"{projectId}/{workItemId}/{logicalName}_V{Math.Max(1, revision)}.json";

    public static string LatestPointer(string projectId, string workItemId) =>
        $"{projectId}/{workItemId}/LATEST.json";

    public static string StateContext(string projectId, string workItemId) =>
        $"{projectId}/{workItemId}/state-context.json";

    public static string AuditVersioned(string projectId, string workItemId, int revision) =>
        $"{projectId}/{workItemId}/AuditLog_V{Math.Max(1, revision)}.json";

    /// <summary>Map registry key → logical file stem for named version copies.</summary>
    public static string? LogicalStem(string artifactKey) => artifactKey switch
    {
        "prd" => "PRD",
        "prdMd" => "PRD_MD",
        "techSpec" => "SYSTEM_DESIGN",
        "techSpecMd" => "SYSTEM_DESIGN_MD",
        "implementationPlan" => "IA_PLAN",
        "codeState" => "CODE_STATE",
        "codeGraph" => "CODE_GRAPH",
        "review" => "IA_REVIEW",
        "testResults" => "IA_TEST_RESULTS",
        "pullRequest" => "PULL_REQUEST",
        "taTestPlan" => "TA_TEST_PLAN",
        "taResults" => "TA_RESULTS",
        "taReport" => "TA_REPORT",
        "taStatus" => "TA_STATUS",
        "taConcerns" => "TA_CONCERNS",
        "merge" => "MERGE",
        "completionSummary" => "COMPLETION",
        "sandboxUrl" => "SANDBOX_URL",
        "stateContext" => "STATE_CONTEXT",
        _ => null
    };
}
