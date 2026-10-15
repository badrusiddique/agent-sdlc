namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>Wraps <see cref="LocalComposeHost"/> so deploy is swappable with App Service.</summary>
public sealed class LocalComposeDeployHost : IDeployHost
{
    public Task<DeployOutcome> DeployAsync(
        string workspace,
        string workItemId,
        CancellationToken cancellationToken) =>
        LocalComposeHost.DeployAsync(workspace, workItemId, cancellationToken);

    public Task FinalizeAsync(string workspace, CancellationToken cancellationToken) =>
        LocalComposeHost.FinalizeAsync(workspace, cancellationToken);
}
