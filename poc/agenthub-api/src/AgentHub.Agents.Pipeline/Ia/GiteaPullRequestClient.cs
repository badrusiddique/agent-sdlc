using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentHub.Agents.Pipeline.Ia;

public sealed record PullRequestResult(
    string Provider,
    string PrUrl,
    int? PrNumber,
    string Branch,
    string HeadCommit,
    string Detail);

/// <summary>
/// Opens a PR against the local Gitea mono-repo (AzDo stand-in).
/// Falls back to a local bare remote + synthetic PR metadata when Gitea is down.
/// </summary>
public sealed class GiteaPullRequestClient
{
    private readonly IaGitOptions _options;
    private readonly HttpClient _http;

    public GiteaPullRequestClient(IaGitOptions options, HttpClient? http = null)
    {
        _options = options;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<PullRequestResult> PushAndOpenPrAsync(
        string monoRoot,
        string branch,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.Equals(_options.Provider, "none", StringComparison.OrdinalIgnoreCase))
        {
            var head = await GitRevAsync(monoRoot, cancellationToken).ConfigureAwait(false);
            return new PullRequestResult(
                "none",
                "local://no-remote",
                null,
                branch,
                head,
                "Git push skipped (Ia:Git:Provider=none).");
        }

        try
        {
            await EnsureGiteaRepoAsync(cancellationToken).ConfigureAwait(false);
            var remoteUrl = BuildAuthenticatedCloneUrl();
            await GitAsync(monoRoot, "remote remove origin", cancellationToken, ignoreExit: true)
                .ConfigureAwait(false);
            await GitAsync(monoRoot, $"remote add origin {remoteUrl}", cancellationToken)
                .ConfigureAwait(false);
            await GitAsync(monoRoot, $"checkout -B {branch}", cancellationToken).ConfigureAwait(false);
            await GitAsync(monoRoot, $"push -u origin HEAD:{branch} --force", cancellationToken)
                .ConfigureAwait(false);
            var head = await GitRevAsync(monoRoot, cancellationToken).ConfigureAwait(false);
            var pr = await CreatePullRequestAsync(branch, title, body, cancellationToken)
                .ConfigureAwait(false);
            return pr with { HeadCommit = head };
        }
        catch (Exception ex)
        {
            var bare = Path.Combine(Path.GetTempPath(), "bgx-poc-monorepo.git");
            if (!Directory.Exists(Path.Combine(bare, "refs")))
            {
                await RunHostAsync(
                        "git",
                        $"init --bare \"{bare}\"",
                        Path.GetTempPath(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await GitAsync(monoRoot, "remote remove origin", cancellationToken, ignoreExit: true)
                .ConfigureAwait(false);
            await GitAsync(monoRoot, $"remote add origin \"{bare}\"", cancellationToken)
                .ConfigureAwait(false);
            await GitAsync(monoRoot, $"checkout -B {branch}", cancellationToken).ConfigureAwait(false);
            await GitAsync(monoRoot, $"push -u origin HEAD:{branch} --force", cancellationToken)
                .ConfigureAwait(false);
            var head = await GitRevAsync(monoRoot, cancellationToken).ConfigureAwait(false);
            return new PullRequestResult(
                "local-bare",
                $"file://{bare}#pr/{branch}",
                null,
                branch,
                head,
                $"Gitea unavailable ({Truncate(ex.Message, 200)}); pushed to local bare remote.");
        }
    }

    private async Task EnsureGiteaRepoAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        using var ping = await _http.GetAsync($"{baseUrl}/api/v1/version", cancellationToken)
            .ConfigureAwait(false);
        if (!ping.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gitea not reachable at {baseUrl}.");
        }

        await EnsureUserAsync(cancellationToken).ConfigureAwait(false);
        var token = await EnsureTokenAsync(cancellationToken).ConfigureAwait(false);
        _options.Token = token;

        using var orgReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orgs");
        ApplyAuth(orgReq, token);
        orgReq.Content = JsonBody(new { username = _options.Owner, visibility = "public" });
        using var orgRes = await _http.SendAsync(orgReq, cancellationToken).ConfigureAwait(false);
        _ = orgRes;

        using var repoReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/api/v1/orgs/{_options.Owner}/repos");
        ApplyAuth(repoReq, token);
        repoReq.Content = JsonBody(new
        {
            name = _options.Repo,
            description = "BuildGenix POC mono-repo (AzDo stand-in)",
            @private = false,
            auto_init = true,
            default_branch = "main"
        });
        using var repoRes = await _http.SendAsync(repoReq, cancellationToken).ConfigureAwait(false);
        _ = repoRes;
    }

    private async Task EnsureUserAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/users/{_options.Username}");
        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (res.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Gitea user '{_options.Username}' missing. Run poc/scripts/seed-gitea.sh first.");
    }

    private async Task<string> EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            return _options.Token!;
        }

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/api/v1/users/{_options.Username}/tokens");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = JsonBody(new
        {
            name = $"ia-poc-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            scopes = new[] { "write:repository", "write:organization", "write:user" }
        });
        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gitea token create failed: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("sha1", out var sha) && sha.GetString() is { Length: > 0 } token)
        {
            return token;
        }

        throw new InvalidOperationException("Gitea token response missing sha1.");
    }

    private async Task<PullRequestResult> CreatePullRequestAsync(
        string branch,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var token = _options.Token ?? await EnsureTokenAsync(cancellationToken).ConfigureAwait(false);
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/api/v1/repos/{_options.Owner}/{_options.Repo}/pulls");
        ApplyAuth(req, token);
        req.Content = new StringContent(
            $$"""
            {"title":{{JsonSerializer.Serialize(title)}},"body":{{JsonSerializer.Serialize(body)}},"head":{{JsonSerializer.Serialize(branch)}},"base":"main"}
            """,
            Encoding.UTF8,
            "application/json");

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            if ((int)res.StatusCode is 409 or 422)
            {
                return new PullRequestResult(
                    "gitea",
                    $"{baseUrl}/{_options.Owner}/{_options.Repo}/pulls",
                    null,
                    branch,
                    "",
                    $"PR may already exist: {Truncate(json, 300)}");
            }

            throw new InvalidOperationException($"Gitea open PR failed: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var number = doc.RootElement.TryGetProperty("number", out var n) ? n.GetInt32() : (int?)null;
        var html = doc.RootElement.TryGetProperty("html_url", out var u)
            ? u.GetString()
            : $"{baseUrl}/{_options.Owner}/{_options.Repo}/pulls/{number}";
        return new PullRequestResult("gitea", html ?? "", number, branch, "", "PR opened on Gitea.");
    }

    private string BuildAuthenticatedCloneUrl()
    {
        var uri = new Uri(_options.CloneUrl);
        var user = Uri.EscapeDataString(_options.Username);
        var pass = Uri.EscapeDataString(_options.Token ?? _options.Password);
        return $"{uri.Scheme}://{user}:{pass}@{uri.Host}:{uri.Port}{uri.AbsolutePath}";
    }

    private static void ApplyAuth(HttpRequestMessage req, string token) =>
        req.Headers.Authorization = new AuthenticationHeaderValue("token", token);

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task GitAsync(
        string cwd,
        string args,
        CancellationToken cancellationToken,
        bool ignoreExit = false)
    {
        await RunHostAsync("git", args, cwd, cancellationToken, ignoreExit).ConfigureAwait(false);
    }

    private static async Task<string> GitRevAsync(string cwd, CancellationToken cancellationToken)
    {
        var (exit, stdout) = await RunHostCaptureAsync(
                "git",
                "rev-parse --short HEAD",
                cwd,
                cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return "unknown";
        }

        return stdout.Trim();
    }

    private static async Task RunHostAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool ignoreExit = false)
    {
        var (exit, output) = await RunHostCaptureAsync(fileName, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0 && !ignoreExit)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed: {Truncate(output, 2000)}");
        }
    }

    private static async Task<(int Exit, string Output)> RunHostCaptureAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, stdout + stderr);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
