namespace AgentHub.Agents.Pipeline.Ia;

public static class DeployHostFactory
{
    public static IDeployHost Create(IaDeployOptions options, HttpClient? http = null)
    {
        if (options.IsAppService)
        {
            return new AzureAppServiceDeployHost(options, http);
        }

        return new LocalComposeDeployHost();
    }
}
