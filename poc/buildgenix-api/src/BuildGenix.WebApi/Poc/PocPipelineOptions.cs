namespace BuildGenix.WebApi.Poc;

public sealed class PocPipelineOptions
{
    public const string SectionName = "PocPipeline";

    public string AgentHubBaseUrl { get; set; } = "http://127.0.0.1:43180";

    public string DummyUserId { get; set; } = "dummy-user";

    public string AuditLogDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "bgx-poc-audit");

    /// <summary>When true, skip Azure Tables and keep state in memory + AuditLog.json.</summary>
    public bool UseInMemoryTables { get; set; } = true;
}
