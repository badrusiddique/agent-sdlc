namespace AgentHub.Infrastructure.Options;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string? ConnectionString { get; set; } =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    /// <summary>
    /// Blob service URI for App Service with DefaultAzureCredential.
    /// Example: https://{account}.blob.core.windows.net
    /// </summary>
    public string? ServiceUri { get; set; }

    public string ContainerName { get; set; } = "agenthub-artifacts";
}
