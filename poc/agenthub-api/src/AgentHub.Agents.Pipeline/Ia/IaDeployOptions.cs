namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>Local Compose stand-in or Azure App Service zip deploy.</summary>
public sealed class IaDeployOptions
{
    public const string SectionName = "Ia:Deploy";

    /// <summary>compose | appservice</summary>
    public string Provider { get; set; } = "compose";

    /// <summary>
    /// App Service auth when Provider=appservice:
    /// publishProfile (local / key) | managedIdentity (cloud / DefaultAzureCredential Bearer to Kudu).
    /// Prefer env IA_DEPLOY_AUTH.
    /// </summary>
    public string Auth { get; set; } = "publishProfile";

    /// <summary>App Service site name (e.g. app-bgx-poc-calc).</summary>
    public string? AppName { get; set; }

    /// <summary>https://{app}.azurewebsites.net — optional override of default hostname.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>https://{app}.scm.azurewebsites.net — optional override.</summary>
    public string? ScmBaseUrl { get; set; }

    /// <summary>Publish-profile user when Auth=publishProfile (often ${AppName}). Prefer env IA_DEPLOY_USER.</summary>
    public string? PublishUser { get; set; }

    /// <summary>Publish-profile password when Auth=publishProfile. Prefer env IA_DEPLOY_PASSWORD. Never commit.</summary>
    public string? PublishPassword { get; set; }

    public bool UseManagedIdentity =>
        string.Equals(Auth, "managedIdentity", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Auth, "mi", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Auth, "defaultazurecredential", StringComparison.OrdinalIgnoreCase);

    /// <summary>Health path after deploy (default /health).</summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>Seconds to wait for /health after zipdeploy.</summary>
    public int HealthTimeoutSeconds { get; set; } = 180;

    public bool IsAppService =>
        string.Equals(Provider, "appservice", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "azure", StringComparison.OrdinalIgnoreCase);

    public bool IsCompose =>
        string.Equals(Provider, "compose", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "local", StringComparison.OrdinalIgnoreCase);
}
