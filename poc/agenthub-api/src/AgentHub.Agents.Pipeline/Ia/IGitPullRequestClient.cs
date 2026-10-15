namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Git + PR + merge port. Local = Gitea; Azure = Azure DevOps Repos. Same caller in IA/TA.
/// </summary>
public interface IGitPullRequestClient
{
    Task<PullRequestResult> PushAndOpenPrAsync(
        string monoRoot,
        string branch,
        string title,
        string body,
        CancellationToken cancellationToken);

    Task<PullRequestResult> MergePullRequestAsync(
        int prNumber,
        string? branchHint,
        CancellationToken cancellationToken);
}
