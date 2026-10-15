namespace AgentHub.Agents.Pipeline.Ia;

public static class GitPullRequestClientFactory
{
    public static IGitPullRequestClient Create(IaGitOptions options, HttpClient? http = null)
    {
        if (options.IsAzureDevOps)
        {
            return new AzureDevOpsPullRequestClient(options, http);
        }

        if (string.Equals(options.Provider, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new NoneGitPullRequestClient();
        }

        // gitea (default) and local-bare fallback live inside GiteaPullRequestClient
        return new GiteaPullRequestClient(options, http);
    }
}

internal sealed class NoneGitPullRequestClient : IGitPullRequestClient
{
    public Task<PullRequestResult> PushAndOpenPrAsync(
        string monoRoot,
        string branch,
        string title,
        string body,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new PullRequestResult("none", "local://no-remote", null, branch, "local", "Git push skipped."));

    public Task<PullRequestResult> MergePullRequestAsync(
        int prNumber,
        string? branchHint,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new PullRequestResult(
                "none",
                "local://no-remote",
                prNumber,
                branchHint ?? "",
                "local",
                "Merge skipped."));
}
