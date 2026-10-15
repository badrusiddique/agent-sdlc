using System.Net;
using System.Text;
using System.Text.Json;
using AgentHub.Agents.Pipeline.Ia;

namespace AgentHub.Agents.Pipeline.Tests;

/// <summary>
/// Contract checks against Microsoft Learn shapes so real-subscription runs
/// do not fail on wrong URLs, auth headers, or JSON fields.
/// </summary>
public sealed class AzureContractVerificationTests
{
    [Fact]
    public async Task Azdo_create_pr_posts_learn_contract_url_and_body()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var handler = new ScriptedHandler();
        handler.Map(
            HttpMethod.Post,
            uri => uri.AbsolutePath.Contains("/pullrequests", StringComparison.Ordinal),
            (req, b) =>
            {
                seen = req;
                body = b;
                return """{"pullRequestId":99,"url":"https://dev.azure.com/o/p/_apis/git/pullRequests/99"}""";
            });

        var client = new AzureDevOpsPullRequestClient(
            new IaGitOptions
            {
                Provider = "azuredevops",
                Organization = "wcbbc",
                Project = "AI Operations",
                Repo = "inno-tech-aisdlc-apps",
                Token = "pat-value",
                TargetBranch = "main",
                ApiVersion = "7.1"
            },
            new HttpClient(handler));

        var method = typeof(AzureDevOpsPullRequestClient).GetMethod(
            "CreatePullRequestAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task<PullRequestResult>)method!.Invoke(
            client,
            ["feature/wi-1", "title", "body", "deadbeef", CancellationToken.None])!;
        var result = await task;

        Assert.Equal(99, result.PrNumber);
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Contains(
            "/wcbbc/AI%20Operations/_apis/git/repositories/inno-tech-aisdlc-apps/pullrequests",
            seen.RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Contains("api-version=7.1", seen.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("Basic", seen.Headers.Authorization?.Scheme);

        // Basic empty-user:token per AzDO PAT docs
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(seen.Headers.Authorization!.Parameter!));
        Assert.Equal(":pat-value", decoded);

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("refs/heads/feature/wi-1", doc.RootElement.GetProperty("sourceRefName").GetString());
        Assert.Equal("refs/heads/main", doc.RootElement.GetProperty("targetRefName").GetString());
        Assert.Equal("title", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("body", doc.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void Azdo_clone_url_encodes_project_spaces()
    {
        var opts = new IaGitOptions
        {
            Provider = "azuredevops",
            Organization = "wcbbc",
            Project = "AI Operations",
            Repo = "app-monorepo"
        };
        Assert.Equal(
            "https://dev.azure.com/wcbbc/AI%20Operations/_git/app-monorepo",
            opts.CloneUrl);
    }

    [Fact]
    public async Task AppService_zipdeploy_then_health_uses_kudu_contract()
    {
        HttpRequestMessage? zipReq = null;
        var handler = new ScriptedHandler();
        handler.Map(
            HttpMethod.Post,
            uri => uri.AbsolutePath.Contains("/api/zipdeploy", StringComparison.Ordinal),
            (req, _) =>
            {
                zipReq = req;
                return "ok";
            });
        handler.Map(
            HttpMethod.Get,
            uri => uri.AbsolutePath.Contains("/health", StringComparison.Ordinal),
            (_, _) => """{"status":"ok"}""");

        var workspace = CreatePublishableBackend();
        var host = new AzureAppServiceDeployHost(
            new IaDeployOptions
            {
                Provider = "appservice",
                AppName = "app-bgx-poc",
                PublicBaseUrl = "https://app-bgx-poc.azurewebsites.net",
                ScmBaseUrl = "https://app-bgx-poc.scm.azurewebsites.net",
                PublishUser = "$app-bgx-poc",
                PublishPassword = "secret",
                HealthPath = "/health",
                HealthTimeoutSeconds = 30
            },
            new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) });

        var outcome = await host.DeployAsync(workspace, "wi1", CancellationToken.None);

        Assert.Equal("https://app-bgx-poc.azurewebsites.net", outcome.Url);
        Assert.Equal("appservice-zipdeploy", outcome.Mode);
        Assert.NotNull(zipReq);
        Assert.Equal(
            "https://app-bgx-poc.scm.azurewebsites.net/api/zipdeploy",
            zipReq!.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", zipReq.Headers.Authorization?.Scheme);
        var raw = Encoding.ASCII.GetString(Convert.FromBase64String(zipReq.Headers.Authorization!.Parameter!));
        Assert.Equal("$app-bgx-poc:secret", raw);
    }

    [Fact]
    public void Factories_default_to_local_standins()
    {
        Assert.IsType<GiteaPullRequestClient>(GitPullRequestClientFactory.Create(new IaGitOptions()));
        Assert.IsType<LocalComposeDeployHost>(DeployHostFactory.Create(new IaDeployOptions()));
    }

    [Fact]
    public void Azure_providers_selected_by_config()
    {
        Assert.IsType<AzureDevOpsPullRequestClient>(
            GitPullRequestClientFactory.Create(
                new IaGitOptions { Provider = "azuredevops", Organization = "o", Token = "t" }));
        Assert.IsType<AzureAppServiceDeployHost>(
            DeployHostFactory.Create(new IaDeployOptions { Provider = "appservice", AppName = "a" }));
    }

    private static string CreatePublishableBackend()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-as-contract-" + Guid.NewGuid().ToString("N"));
        var backend = Path.Combine(root, "backend");
        Directory.CreateDirectory(backend);
        File.WriteAllText(
            Path.Combine(backend, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(backend, "Program.cs"),
            """
            var b = WebApplication.CreateBuilder(args);
            var a = b.Build();
            a.MapGet("/health", () => Results.Ok(new { status = "ok" }));
            a.Run();
            """);
        return root;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, Func<Uri, bool> Match, Func<HttpRequestMessage, string?, string> Respond)>
            _maps = new();

        public void Map(
            HttpMethod method,
            Func<Uri, bool> match,
            Func<HttpRequestMessage, string?, string> respond) =>
            _maps.Add((method, match, respond));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
            {
                // Avoid decoding zip as text
                if (request.Content.Headers.ContentType?.MediaType == "application/octet-stream")
                {
                    body = "<binary>";
                }
                else
                {
                    body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var map in _maps)
            {
                if (map.Method == request.Method && map.Match(request.RequestUri!))
                {
                    var responseBody = map.Respond(request, body);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                    };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"no script for {request.Method} {request.RequestUri}")
            };
        }
    }
}
