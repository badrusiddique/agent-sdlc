namespace AgentHub.Agents.Healthy.Abstractions;

public interface IArtifactBlobStore
{
    Task WriteJsonAsync(
        string blobPath,
        string jsonBody,
        CancellationToken cancellationToken = default);
}
