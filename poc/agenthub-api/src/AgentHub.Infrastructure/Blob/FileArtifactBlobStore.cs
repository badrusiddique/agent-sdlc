using System.Text;
using AgentHub.Core.Abstractions;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Infrastructure.Blob;

/// <summary>Local filesystem blob store for the POC when Azurite is not running.</summary>
public sealed class FileArtifactBlobStore : IArtifactBlobStore
{
    private readonly string _root;
    private readonly ILogger<FileArtifactBlobStore> _logger;

    public FileArtifactBlobStore(IOptions<BlobStorageOptions> options, ILogger<FileArtifactBlobStore> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _root = !string.IsNullOrWhiteSpace(opts.FileRootPath)
            ? opts.FileRootPath
            : Path.Combine(Path.GetTempPath(), "bgx-poc-blobs");
        Directory.CreateDirectory(_root);
    }

    public async Task WriteJsonAsync(
        string blobPath,
        string jsonBody,
        CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(_root, blobPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, jsonBody, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Wrote file artifact {BlobPath}", blobPath);
    }

    public async Task<string?> TryReadJsonAsync(
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(_root, blobPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            return null;
        }

        return await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
    }
}
