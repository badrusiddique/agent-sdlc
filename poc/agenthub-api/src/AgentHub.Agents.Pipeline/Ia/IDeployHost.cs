namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>DEV deploy port. Local = Compose; Azure = App Service zipdeploy.</summary>
public interface IDeployHost
{
    Task<DeployOutcome> DeployAsync(
        string workspace,
        string workItemId,
        CancellationToken cancellationToken);

    Task FinalizeAsync(string workspace, CancellationToken cancellationToken);
}
