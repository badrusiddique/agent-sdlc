namespace BuildGenix.WebApi.Poc;

public sealed class PocPipelineOptions
{
    public const string SectionName = "PocPipeline";

    public string AgentHubBaseUrl { get; set; } = "http://127.0.0.1:43180";

    public string DummyUserId { get; set; } = "dummy-user";

    public string AuditLogDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "bgx-poc-audit");

    /// <summary>
    /// Local file-blob root mirrored from AgentHub FileArtifactBlobStore (Development).
    /// Used to serve Docs without round-tripping when blobs are on disk.
    /// </summary>
    public string ArtifactFileRootPath { get; set; } =
        Path.Combine(Path.GetTempPath(), "bgx-poc-blobs");

    /// <summary>When true, skip Azure Tables and keep state in memory + AuditLog.json.</summary>
    public bool UseInMemoryTables { get; set; } = true;

    /// <summary>Model id passed on each task (null/empty lets AgentHub use its provider default).</summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// When true (default), BuildGenix only intakes + enqueues PRD on the agent bus;
    /// PRD/SD/IA/TA/Deploy BackgroundServices chain via Service Bus topics and REST-callback results.
    /// When false, legacy synchronous HTTP /dispatch stage loop is used.
    /// </summary>
    public bool UseTopicAgents { get; set; } = true;
}
