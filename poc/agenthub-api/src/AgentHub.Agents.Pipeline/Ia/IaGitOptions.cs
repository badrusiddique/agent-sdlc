namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>Local Gitea (AzDo stand-in) or later real Azure DevOps / GitHub.</summary>
public sealed class IaGitOptions
{
    public const string SectionName = "Ia:Git";

    /// <summary>gitea | local-bare | none</summary>
    public string Provider { get; set; } = "gitea";

    public string BaseUrl { get; set; } = "http://127.0.0.1:43185";

    public string Owner { get; set; } = "buildgenix";

    public string Repo { get; set; } = "app-monorepo";

    public string Username { get; set; } = "poc";

    public string Password { get; set; } = "pocpocpoc";

    public string? Token { get; set; }

    public string CloneUrl =>
        $"{BaseUrl.TrimEnd('/')}/{Owner}/{Repo}.git";
}
