namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>Local Gitea (AzDo stand-in) or real Azure DevOps / none.</summary>
public sealed class IaGitOptions
{
    public const string SectionName = "Ia:Git";

    /// <summary>gitea | azuredevops | local-bare | none</summary>
    public string Provider { get; set; } = "gitea";

    /// <summary>
    /// Azure DevOps auth when Provider=azuredevops:
    /// pat (local / key) | managedIdentity (cloud App Service / az login via DefaultAzureCredential).
    /// Prefer env IA_GIT_AUTH.
    /// </summary>
    public string Auth { get; set; } = "pat";

    /// <summary>
    /// Gitea: http://127.0.0.1:43185
    /// Azure DevOps: https://dev.azure.com/{organization} (org only; project/repo below)
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:43185";

    /// <summary>Gitea owner, or Azure DevOps project name when Organization is set.</summary>
    public string Owner { get; set; } = "buildgenix";

    /// <summary>Azure DevOps organization (required when Provider=azuredevops).</summary>
    public string? Organization { get; set; }

    /// <summary>Azure DevOps project. Falls back to Owner when empty.</summary>
    public string? Project { get; set; }

    public string Repo { get; set; } = "app-monorepo";

    public string Username { get; set; } = "poc";

    public string Password { get; set; } = "pocpocpoc";

    /// <summary>PAT for Azure DevOps when Auth=pat (Code Read/Write + PR). Prefer env IA_GIT_TOKEN.</summary>
    public string? Token { get; set; }

    public bool UseManagedIdentity =>
        string.Equals(Auth, "managedIdentity", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Auth, "mi", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Auth, "defaultazurecredential", StringComparison.OrdinalIgnoreCase);

    public string ApiVersion { get; set; } = "7.1";

    public string TargetBranch { get; set; } = "main";

    public string AzdoProject =>
        string.IsNullOrWhiteSpace(Project) ? Owner : Project!;

    public string CloneUrl
    {
        get
        {
            if (IsAzureDevOps)
            {
                var org = Organization ?? throw new InvalidOperationException(
                    "Ia:Git:Organization is required when Provider=azuredevops.");
                return
                    $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(AzdoProject)}/_git/{Uri.EscapeDataString(Repo)}";
            }

            return $"{BaseUrl.TrimEnd('/')}/{Owner}/{Repo}.git";
        }
    }

    public bool IsAzureDevOps =>
        string.Equals(Provider, "azuredevops", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "azdo", StringComparison.OrdinalIgnoreCase);

    public bool IsGitea =>
        string.Equals(Provider, "gitea", StringComparison.OrdinalIgnoreCase);
}
