using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AgentHub.Agents.Pipeline;

/// <summary>
/// Local deploy adapter for the POC. Prefers docker compose when available;
/// otherwise hosts the generated .NET backend as a process (LocalCompose).
/// </summary>
public static class LocalComposeHost
{
    public static async Task<DeployOutcome> DeployAsync(
        string workspace,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var backend = Path.Combine(workspace, "backend");
        if (!Directory.Exists(backend))
        {
            throw new InvalidOperationException($"Backend workspace missing at {backend}.");
        }

        var port = GetFreePort();
        var url = $"http://127.0.0.1:{port}";
        var composeFile = Path.Combine(workspace, "docker-compose.yml");

        if (File.Exists(composeFile) && HasDocker())
        {
            await RunAsync(
                    "docker",
                    $"compose -f \"{composeFile}\" up -d --build",
                    workspace,
                    cancellationToken)
                .ConfigureAwait(false);
            var mapped = await TryReadComposeUrlAsync(composeFile, cancellationToken).ConfigureAwait(false);
            return new DeployOutcome(mapped ?? url, "docker-compose", null);
        }

        var csproj = Directory.EnumerateFiles(backend, "*.csproj")
                         .Where(p => !p.Contains(".Tests", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.Ordinal)
                         .FirstOrDefault()
                     ?? throw new InvalidOperationException("No .csproj in backend workspace.");

        var dotnet = ResolveDotnet();
        var (buildExit, buildOut) = await RunCaptureAsync(
                dotnet,
                $"build \"{csproj}\" -c Release --nologo",
                backend,
                cancellationToken)
            .ConfigureAwait(false);
        if (buildExit != 0)
        {
            throw new InvalidOperationException($"Local deploy build failed: {Truncate(buildOut, 2000)}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            Arguments = $"run --project \"{csproj}\" -c Release --no-build --urls {url}",
            WorkingDirectory = backend,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["OLLAMA_BASE_URL"] = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
                                                  ?? "http://127.0.0.1:11434";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start local backend process.");
        }

        var healthy = await WaitForHealthyAsync(url, TimeSpan.FromSeconds(90), cancellationToken)
            .ConfigureAwait(false);
        if (!healthy)
        {
            string stdout = string.Empty;
            string stderr = string.Empty;
            try
            {
                process.Kill(entireProcessTree: true);
                stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }

            throw new InvalidOperationException(
                $"Local deploy smoke failed for {url}/health. stdout={Truncate(stdout, 800)} stderr={Truncate(stderr, 800)}");
        }

        var pidPath = Path.Combine(workspace, ".deploy-pid");
        await File.WriteAllTextAsync(pidPath, process.Id.ToString(), cancellationToken).ConfigureAwait(false);
        var metaPath = Path.Combine(workspace, ".deploy-meta.json");
        await File.WriteAllTextAsync(
                metaPath,
                JsonSerializer.Serialize(new { url, pid = process.Id, workItemId }),
                cancellationToken)
            .ConfigureAwait(false);

        return new DeployOutcome(url, "process", process.Id);
    }

    public static async Task FinalizeAsync(string workspace, CancellationToken cancellationToken)
    {
        var metaPath = Path.Combine(workspace, ".deploy-meta.json");
        if (!File.Exists(metaPath))
        {
            return;
        }

        // Keep the process running so the URL stays live for the demo; Finalize only records merge.
        await File.WriteAllTextAsync(
                Path.Combine(workspace, ".finalized"),
                DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> WaitForHealthyAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await client.GetAsync($"{baseUrl}/health", cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool HasDocker()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunAsync(
        string fileName,
        string args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var (exit, output) = await RunCaptureAsync(fileName, args, workingDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"{fileName} {args} failed: {Truncate(output, 2000)}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunCaptureAsync(
        string fileName,
        string args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (p.ExitCode, stdout + stderr);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";

    private static Task<string?> TryReadComposeUrlAsync(string composeFile, CancellationToken cancellationToken)
    {
        // Convention in our templates: host port 43142 (API).
        _ = composeFile;
        _ = cancellationToken;
        return Task.FromResult<string?>("http://127.0.0.1:43142");
    }

    private static string ResolveDotnet()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var candidate = Path.Combine(fromEnv, "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed record DeployOutcome(string Url, string Mode, int? ProcessId);
