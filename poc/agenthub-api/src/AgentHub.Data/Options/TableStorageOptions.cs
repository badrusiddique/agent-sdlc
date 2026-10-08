namespace AgentHub.Data.Options;

public sealed class TableStorageOptions
{
    public const string SectionName = "TableStorage";

    /// <summary>Azure (default) or Memory for the POC (skips Azurite).</summary>
    public string Provider { get; set; } = "Azure";

    /// <summary>
    /// Azurite or Azure Storage connection string. Preferred for local Development.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Table service URI for non-Development (App Service) with DefaultAzureCredential.
    /// Example: https://{account}.table.core.windows.net
    /// </summary>
    public string? ServiceUri { get; set; }

    /// <summary>
    /// Optional prefix applied to every table name (harness / shared-account isolation).
    /// </summary>
    public string StoragePrefix { get; set; } = string.Empty;

    public Dictionary<string, string> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
