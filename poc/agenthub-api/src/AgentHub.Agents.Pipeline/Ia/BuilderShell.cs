using System.Diagnostics;
using System.Text;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Runs build/test commands inside the IA builder container when Docker is available;
/// otherwise runs the same command on the host (SDK required).
/// </summary>
public static class BuilderShell
{
    public const string ImageName = "agenthub-ia-builder:local";

    public static bool DockerAvailable()
    {
        try
        {
            var (exit, _) = RunCapture("docker", "version --format '{{.Server.Version}}'", Directory.GetCurrentDirectory(), null);
            return exit == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> ImagePresentAsync(CancellationToken cancellationToken)
    {
        if (!DockerAvailable())
        {
            return false;
        }

        var (inspectExit, _) = await RunCaptureAsync(
                "docker",
                $"image inspect {ImageName}",
                Directory.GetCurrentDirectory(),
                cancellationToken)
            .ConfigureAwait(false);
        return inspectExit == 0;
    }

    /// <summary>Build the builder image (call from scripts). IA does not auto-build on the hot path.</summary>
    public static async Task BuildImageAsync(CancellationToken cancellationToken)
    {
        if (!DockerAvailable())
        {
            throw new InvalidOperationException("Docker is not available.");
        }

        var dockerDir = ResolveBuilderDockerDir()
                        ?? throw new InvalidOperationException("ia-builder Dockerfile directory not found.");
        var (buildExit, buildOut) = await RunCaptureAsync(
                "docker",
                $"build -t {ImageName} \"{dockerDir}\"",
                dockerDir,
                cancellationToken)
            .ConfigureAwait(false);
        if (buildExit != 0)
        {
            throw new InvalidOperationException($"Failed to build {ImageName}: {Truncate(buildOut, 2000)}");
        }
    }

    public static async Task<(int ExitCode, string Output)> RunAsync(
        string monoRoot,
        string command,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? env = null)
    {
        // Prefer pre-built builder image; otherwise run on the host SDK so e2e is not blocked.
        if (DockerAvailable() && await ImagePresentAsync(cancellationToken).ConfigureAwait(false))
        {
            var envArgs = new StringBuilder();
            if (env is not null)
            {
                foreach (var pair in env)
                {
                    envArgs.Append(" -e ").Append(pair.Key).Append('=').Append(Quote(pair.Value));
                }
            }

            var args =
                $"run --rm --network host -v \"{monoRoot}:/work\" -w /work{envArgs} {ImageName} bash -lc {Quote(command)}";
            return await RunCaptureAsync("docker", args, monoRoot, cancellationToken, env: null)
                .ConfigureAwait(false);
        }

        return await RunCaptureAsync("bash", $"-lc {Quote(command)}", monoRoot, cancellationToken, env)
            .ConfigureAwait(false);
    }

    private static string? ResolveBuilderDockerDir()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docker", "ia-builder"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docker", "ia-builder")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "docker", "ia-builder")),
            "/workspace/poc/agenthub-api/docker/ia-builder"
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static (int ExitCode, string Output) RunCapture(
        string fileName,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? env)
    {
        return RunCaptureAsync(fileName, arguments, workingDirectory, CancellationToken.None, env)
            .GetAwaiter()
            .GetResult();
    }

    private static async Task<(int ExitCode, string Output)> RunCaptureAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? env = null)
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
        if (env is not null)
        {
            foreach (var pair in env)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                sb.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                sb.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {fileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, sb.ToString());
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
