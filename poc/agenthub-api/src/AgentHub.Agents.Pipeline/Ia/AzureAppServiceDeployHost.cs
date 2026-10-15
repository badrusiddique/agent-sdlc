using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Azure.Identity;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// DEV deploy to Azure App Service via Kudu zipdeploy.
/// Auth = publish-profile basic auth locally (key), or managed identity /
/// DefaultAzureCredential Bearer when hosted in Azure.
/// Docs: https://learn.microsoft.com/en-us/azure/app-service/deploy-zip
/// </summary>
public sealed class AzureAppServiceDeployHost : IDeployHost
{
    /// <summary>ARM / App Service management scope used for SCM Bearer auth.</summary>
    public const string ManagementScope = "https://management.azure.com/.default";

    private readonly IaDeployOptions _options;
    private readonly HttpClient _http;
    private readonly TokenCredential _credential;

    public AzureAppServiceDeployHost(
        IaDeployOptions options,
        HttpClient? http = null,
        TokenCredential? credential = null)
    {
        _options = options;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _credential = credential ?? new DefaultAzureCredential();
    }

    public async Task<DeployOutcome> DeployAsync(
        string workspace,
        string workItemId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var backend = Path.Combine(workspace, "backend");
        if (!Directory.Exists(backend))
        {
            throw new InvalidOperationException($"Backend workspace missing at {backend}.");
        }

        var csproj = Directory.EnumerateFiles(backend, "*.csproj")
                         .Where(p => !p.Contains(".Tests", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.Ordinal)
                         .FirstOrDefault()
                     ?? throw new InvalidOperationException("No .csproj in backend workspace.");

        var publishDir = Path.Combine(Path.GetTempPath(), "bgx-as-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(publishDir);
        var zipPath = Path.Combine(Path.GetTempPath(), "bgx-as-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await RunAsync(
                    "dotnet",
                    $"publish \"{csproj}\" -c Release -o \"{publishDir}\" --nologo",
                    backend,
                    cancellationToken)
                .ConfigureAwait(false);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            System.IO.Compression.ZipFile.CreateFromDirectory(publishDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            var scm = ResolveScmBaseUrl().TrimEnd('/');
            var zipUrl = $"{scm}/api/zipdeploy";
            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, cancellationToken).ConfigureAwait(false));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var req = new HttpRequestMessage(HttpMethod.Post, zipUrl) { Content = content };
            await ApplyAuthAsync(req, cancellationToken).ConfigureAwait(false);
            using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"App Service zipdeploy failed ({(int)res.StatusCode}): {Truncate(body, 800)}. Check Auth=publishProfile credentials or managed identity SCM rights. See poc/docs/azure-clickops-integration.md.");
            }

            var publicUrl = ResolvePublicBaseUrl().TrimEnd('/');
            var healthy = await WaitForHealthyAsync(
                    publicUrl + NormalizeHealthPath(_options.HealthPath),
                    TimeSpan.FromSeconds(Math.Max(30, _options.HealthTimeoutSeconds)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!healthy)
            {
                throw new InvalidOperationException(
                    $"App Service deployed but health check failed at {publicUrl}{_options.HealthPath}.");
            }

            return new DeployOutcome(publicUrl, "appservice-zipdeploy", null);
        }
        finally
        {
            TryDelete(publishDir, recursive: true);
            TryDelete(zipPath, recursive: false);
        }
    }

    public Task FinalizeAsync(string workspace, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AppName) && string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            throw new InvalidOperationException(
                "Ia:Deploy:AppName or PublicBaseUrl is required for Provider=appservice. See poc/docs/azure-clickops-integration.md.");
        }

        if (_options.UseManagedIdentity)
        {
            return;
        }

        var user = ResolveUser();
        var pass = ResolvePassword();
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            throw new InvalidOperationException(
                "Ia:Deploy publish credentials missing when Auth=publishProfile. Set Ia:Deploy:PublishUser/PublishPassword or env IA_DEPLOY_USER / IA_DEPLOY_PASSWORD, or set Auth=managedIdentity for cloud. See poc/docs/azure-clickops-integration.md.");
        }
    }

    private string ResolvePublicBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl.TrimEnd('/');
        }

        return $"https://{_options.AppName}.azurewebsites.net";
    }

    private string ResolveScmBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.ScmBaseUrl))
        {
            return _options.ScmBaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(_options.AppName))
        {
            throw new InvalidOperationException("Ia:Deploy:AppName or ScmBaseUrl required for zipdeploy.");
        }

        return $"https://{_options.AppName}.scm.azurewebsites.net";
    }

    private string ResolveUser() =>
        FirstNonEmpty(
            _options.PublishUser,
            Environment.GetEnvironmentVariable("IA_DEPLOY_USER"))
        ?? "";

    private string ResolvePassword() =>
        FirstNonEmpty(
            _options.PublishPassword,
            Environment.GetEnvironmentVariable("IA_DEPLOY_PASSWORD"))
        ?? "";

    private async Task ApplyAuthAsync(HttpRequestMessage req, CancellationToken cancellationToken)
    {
        if (_options.UseManagedIdentity)
        {
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return;
        }

        var raw = $"{ResolveUser()}:{ResolvePassword()}";
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var result = await _credential
            .GetTokenAsync(new TokenRequestContext([ManagementScope]), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.Token))
        {
            throw new InvalidOperationException(
                "DefaultAzureCredential returned an empty token for App Service SCM. Assign Website Contributor (or equivalent) to the managed identity; locally use Auth=publishProfile or az login.");
        }

        return result.Token;
    }

    private static string NormalizeHealthPath(string path) =>
        path.StartsWith('/') ? path : "/" + path;

    private async Task<bool> WaitForHealthyAsync(
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var res = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task RunAsync(
        string file,
        string args,
        string cwd,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {file}");
        var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"{file} {args} failed: {Truncate(stdout + stderr, 1000)}");
        }
    }

    private static void TryDelete(string path, bool recursive)
    {
        try
        {
            if (recursive && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else if (!recursive && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort
        }
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
