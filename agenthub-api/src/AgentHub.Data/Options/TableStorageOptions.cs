namespace AgentHub.Data.Options;

public sealed class TableStorageOptions
{
    public const string SectionName = "TableStorage";

    /// <summary>
    /// Azurite or Azure Storage connection string. Preferred for local Development.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Optional prefix applied to every table name (harness / shared-account isolation).
    /// </summary>
    public string StoragePrefix { get; set; } = string.Empty;

    public Dictionary<string, string> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
