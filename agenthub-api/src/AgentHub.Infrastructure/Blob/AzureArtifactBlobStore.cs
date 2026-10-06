using System.Text;
using AgentHub.Core.Abstractions;
using AgentHub.Infrastructure.Options;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Infrastructure.Blob;

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
        var service = CreateServiceClient(opts);
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

    private static BlobServiceClient CreateServiceClient(BlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUri))
        {
            return new BlobServiceClient(new Uri(options.ServiceUri), new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "BlobStorage:ConnectionString (local/Azurite) or BlobStorage:ServiceUri (DefaultAzureCredential) is required.");
    }
}
