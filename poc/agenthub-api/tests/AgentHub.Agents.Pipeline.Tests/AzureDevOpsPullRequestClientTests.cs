using System.Net;
using System.Text;
using AgentHub.Agents.Pipeline.Ia;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class AzureDevOpsPullRequestClientTests
{
    [Fact]
    public async Task MergePullRequestAsync_completes_active_pr_with_noFastForward()
    {
        var handler = new ScriptedHandler();
        handler.On(
            HttpMethod.Get,
            _ => true,
            """
            {"status":"active","pullRequestId":42,"lastMergeSourceCommit":{"commitId":"abc123"}}
            """);
        string? patchBody = null;
        handler.On(
            HttpMethod.Patch,
            _ => true,
            """
            {"status":"completed","pullRequestId":42,"lastMergeCommit":{"commitId":"merge999"}}
            """,
            captureBody: body => patchBody = body);

        var client = new AzureDevOpsPullRequestClient(
            new IaGitOptions
            {
                Provider = "azuredevops",
                Organization = "wcbbc",
                Project = "AI-Operations",
                Repo = "inno-tech-aisdlc-apps",
                Token = "fake-pat"
            },
            new HttpClient(handler));

        var result = await client.MergePullRequestAsync(42, "feature/x", CancellationToken.None);

        Assert.Equal("azuredevops", result.Provider);
        Assert.Equal(42, result.PrNumber);
        Assert.Equal("merge999", result.HeadCommit);
        Assert.Contains("noFastForward", patchBody, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", patchBody, StringComparison.Ordinal);
        Assert.Contains("abc123", patchBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MergePullRequestAsync_is_idempotent_when_already_completed()
    {
        var handler = new ScriptedHandler();
        handler.On(
            HttpMethod.Get,
            _ => true,
            """
            {"status":"completed","pullRequestId":7,"lastMergeCommit":{"commitId":"done1"}}
            """);

        var client = new AzureDevOpsPullRequestClient(
            new IaGitOptions
            {
                Provider = "azuredevops",
                Organization = "wcbbc",
                Project = "AI-Operations",
                Repo = "app",
                Token = "fake-pat"
            },
            new HttpClient(handler));

        var result = await client.MergePullRequestAsync(7, null, CancellationToken.None);

        Assert.Contains("already completed", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("done1", result.HeadCommit);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Create_requires_organization_and_token()
    {
        var client = new AzureDevOpsPullRequestClient(
            new IaGitOptions { Provider = "azuredevops", Repo = "r" },
            new HttpClient(new ScriptedHandler()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PushAndOpenPrAsync("/tmp", "b", "t", "body", CancellationToken.None));
        Assert.Contains("Organization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_selects_azuredevops_and_gitea()
    {
        var azdo = GitPullRequestClientFactory.Create(
            new IaGitOptions { Provider = "azuredevops", Organization = "o", Token = "t" });
        Assert.IsType<AzureDevOpsPullRequestClient>(azdo);

        var gitea = GitPullRequestClientFactory.Create(new IaGitOptions { Provider = "gitea" });
        Assert.IsType<GiteaPullRequestClient>(gitea);
    }

    [Fact]
    public void Deploy_factory_selects_compose_and_appservice()
    {
        Assert.IsType<LocalComposeDeployHost>(DeployHostFactory.Create(new IaDeployOptions { Provider = "compose" }));
        Assert.IsType<AzureAppServiceDeployHost>(
            DeployHostFactory.Create(new IaDeployOptions { Provider = "appservice", AppName = "x" }));
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, Func<Uri, bool> Match, string Body, Action<string?>? Capture)> _scripts =
            new();

        public int RequestCount { get; private set; }

        public void On(
            HttpMethod method,
            Func<Uri, bool> match,
            string body,
            Action<string?>? captureBody = null) =>
            _scripts.Add((method, match, body, captureBody));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            foreach (var script in _scripts)
            {
                if (script.Method == request.Method && script.Match(request.RequestUri!))
                {
                    script.Capture?.Invoke(body);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(script.Body, Encoding.UTF8, "application/json")
                    };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("no script", Encoding.UTF8, "text/plain")
            };
        }
    }
}
