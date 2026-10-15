namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Mono-repo folder naming for one work item:
/// project-{projectId}-work-item-{workItemId}/{frontend,backend}.
/// </summary>
public static class MonoRepoLayout
{
    public static string FolderName(string projectId, string workItemId) =>
        $"project-{Sanitize(projectId)}-work-item-{Sanitize(workItemId)}";

    public static string ResolveMonoRoot(string projectId, string workItemId, string? workspaceRoot = null)
    {
        // Explicit WorkspaceRoot from the orchestrator / tests is the mono folder itself.
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var trimmed = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(trimmed);
            return trimmed;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "bgx-poc",
            FolderName(projectId, workItemId));
        Directory.CreateDirectory(root);
        return root;
    }

    public static string Frontend(string monoRoot) => Path.Combine(monoRoot, "frontend");

    public static string Backend(string monoRoot) => Path.Combine(monoRoot, "backend");

    public static string WorkBranch(string projectId, string workItemId) =>
        $"feature/{FolderName(projectId, workItemId)}";

    public static void EnsureFrontendFromWwwroot(string monoRoot)
    {
        var frontend = Frontend(monoRoot);
        var wwwroot = Path.Combine(Backend(monoRoot), "wwwroot");
        Directory.CreateDirectory(frontend);

        if (!Directory.Exists(wwwroot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(wwwroot, file);
            var dest = Path.Combine(frontend, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (!File.Exists(dest))
            {
                File.Copy(file, dest, overwrite: false);
            }
        }
    }

    public static void SyncFrontendToWwwroot(string monoRoot)
    {
        var frontend = Frontend(monoRoot);
        var wwwroot = Path.Combine(Backend(monoRoot), "wwwroot");
        if (!Directory.Exists(frontend))
        {
            return;
        }

        Directory.CreateDirectory(wwwroot);
        foreach (var file in Directory.EnumerateFiles(frontend, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(frontend, file);
            var dest = Path.Combine(wwwroot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    public static void WriteReadme(string monoRoot, string projectId, string workItemId, string userRequest)
    {
        var path = Path.Combine(monoRoot, "README.md");
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(
            path,
            $"""
            # {FolderName(projectId, workItemId)}

            Work-item slice in the BuildGenix mono-repo.

            - `frontend/` — UI surface
            - `backend/` — .NET API
            - `tests/` — automated tests (when present)

            Request: {userRequest}
            """);
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "x" : cleaned;
    }
}
