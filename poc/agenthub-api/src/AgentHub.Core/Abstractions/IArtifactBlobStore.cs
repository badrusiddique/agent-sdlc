namespace AgentHub.Core.Abstractions;

public interface IArtifactBlobStore
{
    Task WriteJsonAsync(
        string blobPath,
        string jsonBody,
        CancellationToken cancellationToken = default);

    Task<string?> TryReadJsonAsync(
        string blobPath,
        CancellationToken cancellationToken = default);
}
