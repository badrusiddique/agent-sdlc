using System.Text.Json;

namespace AgentHub.Agents.Pipeline;

/// <summary>Copies checked-in fe-be / fe-be-llm templates into a work-item workspace.</summary>
public static class TemplateMaterializer
{
    public static string ResolveTemplateRoot(string stackProfile)
    {
        var id = stackProfile.Equals("fe-be-llm", StringComparison.OrdinalIgnoreCase)
            ? "fe-be-llm"
            : "fe-be";
        var root = Path.Combine(AppContext.BaseDirectory, "Templates", id);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"Template not found at {root}.");
        }

        return root;
    }

    public static void CopyTemplate(string templateRoot, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        CopyDirectory(templateRoot, destination);
    }

    public static void ApplyImplementationHints(string workspace, string stackProfile, string llmJson)
    {
        var headline = "Hello";
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(llmJson) ? "{}" : llmJson);
            if (doc.RootElement.TryGetProperty("headline", out var h) && h.GetString() is { Length: > 0 } text)
            {
                headline = text;
            }
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(llmJson))
            {
                headline = llmJson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault() ?? "Hello";
                if (headline.Length > 80)
                {
                    headline = headline[..80];
                }
            }
        }

        var indexPath = Path.Combine(workspace, "backend", "wwwroot", "index.html");
        if (File.Exists(indexPath))
        {
            var html = File.ReadAllText(indexPath)
                .Replace("{{HEADLINE}}", headline, StringComparison.Ordinal);
            File.WriteAllText(indexPath, html);
        }

        if (stackProfile.Equals("fe-be-llm", StringComparison.OrdinalIgnoreCase))
        {
            var thoughtPath = Path.Combine(workspace, "backend", "ThoughtCopy.txt");
            File.WriteAllText(thoughtPath, headline);
        }
    }

    public static string WriteCodeState(string workspace, string stackProfile)
    {
        var files = Directory
            .EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(workspace, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        return JsonSerializer.Serialize(
            new
            {
                stackProfile,
                fileCount = files.Length,
                files
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
