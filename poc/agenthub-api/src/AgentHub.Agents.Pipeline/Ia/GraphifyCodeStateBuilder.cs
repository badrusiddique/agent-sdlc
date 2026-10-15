using System.Diagnostics;
using System.Text.Json;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Runs the locked command <c>graphify extract . --code-only</c> in the work-item
/// workspace, then publishes raw <c>code-graph.json</c> and a normalised
/// <c>code-state.json</c>. Local AST only — no network, no model.
/// </summary>
public static class GraphifyCodeStateBuilder
{
    public const string LockedCommand = "graphify extract . --code-only";

    public sealed record Result(string CodeGraphJson, string CodeStateJson, string Command, string GraphPath);

    public static async Task<Result> BuildAsync(string workspace, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspace))
        {
            throw new InvalidOperationException($"Workspace missing for graphify: {workspace}");
        }

        var graphify = ResolveGraphifyExecutable();
        var (exit, output) = await RunAsync(
                graphify,
                "extract . --code-only",
                workspace,
                cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"{LockedCommand} failed ({exit}): {Truncate(output, 1500)}. Install: pip install graphifyy (CLI name remains graphify).");
        }

        var graphPath = Path.Combine(workspace, "graphify-out", "graph.json");
        if (!File.Exists(graphPath))
        {
            throw new InvalidOperationException(
                $"{LockedCommand} did not write graphify-out/graph.json. Output: {Truncate(output, 800)}");
        }

        var codeGraphJson = await File.ReadAllTextAsync(graphPath, cancellationToken).ConfigureAwait(false);
        var codeStateJson = Normalize(codeGraphJson, workspace);
        return new Result(codeGraphJson, codeStateJson, LockedCommand, graphPath);
    }

    public static string Normalize(string codeGraphJson, string workspace)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(codeGraphJson) ? "{}" : codeGraphJson);
        var root = doc.RootElement;

        var files = Directory.Exists(workspace)
            ? Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(workspace, p).Replace('\\', '/'))
                .Where(p => !p.StartsWith("graphify-out/", StringComparison.Ordinal)
                            && !p.StartsWith(".git/", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray()
            : [];

        // graphify graph.json uses NetworkX node-link style: nodes + links
        var nodes = CountArray(root, "nodes");
        var edges = Math.Max(CountArray(root, "edges"), CountArray(root, "links"));
        var communities = CountArray(root, "communities");

        return JsonSerializer.Serialize(
            new
            {
                command = LockedCommand,
                workspace = Path.GetFileName(workspace.TrimEnd(Path.DirectorySeparatorChar, '/')),
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                fileCount = files.Length,
                files,
                graph = new { nodes, edges, communities, source = "graphify-out/graph.json" },
                mode = "request-driven"
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static int CountArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.GetArrayLength()
            : 0;

    private static string ResolveGraphifyExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("GRAPHIFY_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        foreach (var candidate in new[]
                 {
                     "graphify",
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         ".local",
                         "bin",
                         "graphify"),
                     "/usr/local/bin/graphify"
                 })
        {
            if (candidate == "graphify")
            {
                return candidate;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "graphify";
    }

    private static async Task<(int Exit, string Output)> RunAsync(
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
        // Ensure user-local pip scripts are visible in Cloud Agent / laptop shells.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localBin = Path.Combine(home, ".local", "bin");
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (Directory.Exists(localBin) && !path.Contains(localBin, StringComparison.Ordinal))
        {
            psi.Environment["PATH"] = localBin + Path.PathSeparator + path;
        }

        using var p = Process.Start(psi)
                     ?? throw new InvalidOperationException($"Failed to start {file}");
        var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (p.ExitCode, stdout + stderr);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
