using System.Diagnostics;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>Shared clone-main → copy work-item folder → commit → push used by Gitea and AzDO.</summary>
public static class MonoRepoGitPush
{
    public static async Task<string> PushWorkItemFolderAsync(
        string monoRoot,
        string branch,
        string authenticatedCloneUrl,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        var cloneDir = Path.Combine(Path.GetTempPath(), "bgx-git-clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cloneDir);
        try
        {
            await RunAsync(
                    "git",
                    $"clone --depth 50 \"{authenticatedCloneUrl}\" \"{cloneDir}\"",
                    Path.GetTempPath(),
                    cancellationToken)
                .ConfigureAwait(false);
            await GitAsync(cloneDir, "config user.email poc@buildgenix.local", cancellationToken)
                .ConfigureAwait(false);
            await GitAsync(cloneDir, "config user.name poc", cancellationToken).ConfigureAwait(false);
            await GitAsync(cloneDir, $"checkout -B {branch}", cancellationToken).ConfigureAwait(false);

            var folderName = new DirectoryInfo(monoRoot).Name;
            var dest = Path.Combine(cloneDir, folderName);
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, recursive: true);
            }

            CopyDirectory(monoRoot, dest);
            var nestedGit = Path.Combine(dest, ".git");
            if (Directory.Exists(nestedGit))
            {
                Directory.Delete(nestedGit, recursive: true);
            }

            await GitAsync(cloneDir, "add -A", cancellationToken).ConfigureAwait(false);
            await GitAsync(cloneDir, $"commit -m \"{Escape(commitMessage)}\" --allow-empty", cancellationToken)
                .ConfigureAwait(false);
            await GitAsync(cloneDir, $"push -u origin HEAD:{branch} --force", cancellationToken)
                .ConfigureAwait(false);
            return await GitRevAsync(cloneDir, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(cloneDir, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    public static async Task<string> GitRevAsync(string cwd, CancellationToken cancellationToken)
    {
        var (exit, output) = await CaptureAsync("git", "rev-parse HEAD", cwd, cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"git rev-parse failed: {output}");
        }

        return output.Trim();
    }

    private static string Escape(string message) =>
        message.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static Task GitAsync(string cwd, string args, CancellationToken cancellationToken) =>
        RunAsync("git", args, cwd, cancellationToken);

    private static async Task RunAsync(
        string file,
        string args,
        string cwd,
        CancellationToken cancellationToken)
    {
        var (exit, output) = await CaptureAsync(file, args, cwd, cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"{file} {args} failed ({exit}): {Truncate(output, 800)}");
        }
    }

    private static async Task<(int Exit, string Output)> CaptureAsync(
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
        return (p.ExitCode, stdout + stderr);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
