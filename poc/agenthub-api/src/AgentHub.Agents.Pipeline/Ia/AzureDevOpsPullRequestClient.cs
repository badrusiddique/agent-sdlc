using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Azure DevOps Repos PR open + complete (merge). Auth = PAT via Basic empty-user:token
/// (docs: https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests).
/// </summary>
public sealed class AzureDevOpsPullRequestClient : IGitPullRequestClient
{
    private readonly IaGitOptions _options;
    private readonly HttpClient _http;

    public AzureDevOpsPullRequestClient(IaGitOptions options, HttpClient? http = null)
    {
        _options = options;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<PullRequestResult> PushAndOpenPrAsync(
        string monoRoot,
        string branch,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var remote = BuildAuthenticatedCloneUrl();
        var folderName = new DirectoryInfo(monoRoot).Name;
        var head = await MonoRepoGitPush
            .PushWorkItemFolderAsync(monoRoot, branch, remote, $"IA: {folderName}", cancellationToken)
            .ConfigureAwait(false);
        return await CreatePullRequestAsync(branch, title, body, head, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PullRequestResult> MergePullRequestAsync(
        int prNumber,
        string? branchHint,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var org = _options.Organization!;
        var project = Uri.EscapeDataString(_options.AzdoProject);
        var repo = Uri.EscapeDataString(_options.Repo);
        var api = $"https://dev.azure.com/{org}/{project}/_apis/git/repositories/{repo}/pullrequests/{prNumber}?api-version={_options.ApiVersion}";

        using var getReq = new HttpRequestMessage(HttpMethod.Get, api);
        ApplyAuth(getReq);
        using var getRes = await _http.SendAsync(getReq, cancellationToken).ConfigureAwait(false);
        var getJson = await getRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!getRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AzDO get PR failed ({(int)getRes.StatusCode}): {Truncate(getJson, 500)}");
        }

        using var getDoc = JsonDocument.Parse(getJson);
        var status = getDoc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var mergeCommit = TryGetCommitId(getDoc.RootElement, "lastMergeCommit")
                              ?? TryGetCommitId(getDoc.RootElement, "lastMergeSourceCommit")
                              ?? "";
            return new PullRequestResult(
                "azuredevops",
                BuildPrWebUrl(prNumber),
                prNumber,
                branchHint ?? "",
                mergeCommit,
                "PR already completed on Azure DevOps.");
        }

        var sourceCommit = TryGetCommitId(getDoc.RootElement, "lastMergeSourceCommit");
        if (string.IsNullOrWhiteSpace(sourceCommit))
        {
            throw new InvalidOperationException(
                "AzDO PR missing lastMergeSourceCommit; cannot complete safely (moved head / not mergeable).");
        }

        var patchBody = JsonSerializer.Serialize(
            new
            {
                status = "completed",
                lastMergeSourceCommit = new { commitId = sourceCommit },
                completionOptions = new
                {
                    mergeStrategy = "noFastForward",
                    deleteSourceBranch = false,
                    transitionWorkItems = true
                }
            });

        using var patchReq = new HttpRequestMessage(HttpMethod.Patch, api);
        ApplyAuth(patchReq);
        patchReq.Content = new StringContent(patchBody, Encoding.UTF8, "application/json");
        using var patchRes = await _http.SendAsync(patchReq, cancellationToken).ConfigureAwait(false);
        var patchJson = await patchRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!patchRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AzDO complete PR failed ({(int)patchRes.StatusCode}): {Truncate(patchJson, 800)}");
        }

        var mergeSha = "";
        try
        {
            using var patchDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(patchJson) ? "{}" : patchJson);
            mergeSha = TryGetCommitId(patchDoc.RootElement, "lastMergeCommit") ?? sourceCommit;
        }
        catch (JsonException)
        {
            mergeSha = sourceCommit;
        }

        return new PullRequestResult(
            "azuredevops",
            BuildPrWebUrl(prNumber),
            prNumber,
            branchHint ?? "",
            mergeSha,
            "PR completed (merged) on Azure DevOps.");
    }

    private async Task<PullRequestResult> CreatePullRequestAsync(
        string branch,
        string title,
        string body,
        string headCommit,
        CancellationToken cancellationToken)
    {
        var org = _options.Organization!;
        var project = Uri.EscapeDataString(_options.AzdoProject);
        var repo = Uri.EscapeDataString(_options.Repo);
        var api =
            $"https://dev.azure.com/{org}/{project}/_apis/git/repositories/{repo}/pullrequests?api-version={_options.ApiVersion}";

        var payload = JsonSerializer.Serialize(
            new
            {
                sourceRefName = $"refs/heads/{branch}",
                targetRefName = $"refs/heads/{_options.TargetBranch}",
                title,
                description = body
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, api);
        ApplyAuth(req);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            // Existing PR for same source branch is recoverable.
            if ((int)res.StatusCode is 409 or 400)
            {
                var existing = await TryFindOpenPrAsync(branch, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    return existing with { HeadCommit = headCommit, Detail = "Reused existing AzDO PR." };
                }
            }

            throw new InvalidOperationException($"AzDO open PR failed ({(int)res.StatusCode}): {Truncate(json, 800)}");
        }

        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.TryGetProperty("pullRequestId", out var pid) ? pid.GetInt32() : (int?)null;
        var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
        var web = id is int n ? BuildPrWebUrl(n) : (url ?? _options.CloneUrl);
        return new PullRequestResult("azuredevops", web, id, branch, headCommit, "PR opened on Azure DevOps.");
    }

    private async Task<PullRequestResult?> TryFindOpenPrAsync(string branch, CancellationToken cancellationToken)
    {
        var org = _options.Organization!;
        var project = Uri.EscapeDataString(_options.AzdoProject);
        var repo = Uri.EscapeDataString(_options.Repo);
        var source = Uri.EscapeDataString($"refs/heads/{branch}");
        var api =
            $"https://dev.azure.com/{org}/{project}/_apis/git/repositories/{repo}/pullrequests?searchCriteria.status=active&searchCriteria.sourceRefName={source}&api-version={_options.ApiVersion}";
        using var req = new HttpRequestMessage(HttpMethod.Get, api);
        ApplyAuth(req);
        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.GetArrayLength() == 0)
        {
            return null;
        }

        var first = arr[0];
        var id = first.TryGetProperty("pullRequestId", out var pid) ? pid.GetInt32() : (int?)null;
        if (id is null)
        {
            return null;
        }

        return new PullRequestResult(
            "azuredevops",
            BuildPrWebUrl(id.Value),
            id,
            branch,
            "",
            "Found existing active AzDO PR.");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Organization))
        {
            throw new InvalidOperationException(
                "Ia:Git:Organization is required for Provider=azuredevops. See poc/docs/azure-clickops-integration.md.");
        }

        var token = ResolveToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Ia:Git:Token (or env IA_GIT_TOKEN) is required for Azure DevOps. Create a PAT with Code Read & Write + Pull Request Read & Write. See poc/docs/azure-clickops-integration.md.");
        }

        _options.Token = token;
    }

    private string ResolveToken() =>
        FirstNonEmpty(
            _options.Token,
            Environment.GetEnvironmentVariable("IA_GIT_TOKEN"),
            Environment.GetEnvironmentVariable("AZDO_PAT"))
        ?? "";

    private string BuildAuthenticatedCloneUrl()
    {
        var token = ResolveToken();
        var org = _options.Organization!;
        // PAT over HTTPS: username can be anything; password is the PAT.
        return $"https://pat:{Uri.EscapeDataString(token)}@dev.azure.com/{org}/{_options.AzdoProject}/_git/{_options.Repo}";
    }

    private string BuildPrWebUrl(int prNumber) =>
        $"https://dev.azure.com/{_options.Organization}/{_options.AzdoProject}/_git/{_options.Repo}/pullrequest/{prNumber}";

    private void ApplyAuth(HttpRequestMessage req)
    {
        var token = ResolveToken();
        var bytes = Encoding.ASCII.GetBytes($":{token}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    private static string? TryGetCommitId(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node))
        {
            return null;
        }

        if (node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty("commitId", out var id))
        {
            return id.GetString();
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
