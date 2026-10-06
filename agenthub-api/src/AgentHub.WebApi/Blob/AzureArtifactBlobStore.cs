using System.Text;
using AgentHub.Agents.Healthy.Abstractions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Blob;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string ConnectionString { get; set; } =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    public string ContainerName { get; set; } = "agenthub-artifacts";
}

public sealed class AzureArtifactBlobStore : IArtifactBlobStore
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureArtifactBlobStore> _logger;

    public AzureArtifactBlobStore(
        IOptions<BlobStorageOptions> options,
        ILogger<AzureArtifactBlobStore> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var service = new BlobServiceClient(opts.ConnectionString);
        _container = service.GetBlobContainerClient(opts.ContainerName);
    }

    public async Task WriteJsonAsync(
        string blobPath,
        string jsonBody,
        CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var blob = _container.GetBlobClient(blobPath);
        var data = BinaryData.FromString(jsonBody);
        await blob
            .UploadAsync(
                data,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                    Conditions = null
                },
                cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Wrote artifact blob {BlobPath} ({Bytes} bytes)",
            blobPath,
            Encoding.UTF8.GetByteCount(jsonBody));
    }
}
