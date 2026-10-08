using System.Reflection;
using System.Text;

namespace AgentHub.Agents.Pipeline;

public static class PromptLoader
{
    private const string ResourceName = "AgentHub.Agents.Pipeline.Prompts.pipeline-system.txt";

    public static string LoadSystemPrompt()
    {
        var full = LoadFullPrompt();
        var marker = "===STAGE:";
        var idx = full.IndexOf(marker, StringComparison.Ordinal);
        return idx < 0 ? full.Trim() : full[..idx].Trim();
    }

    public static string LoadStageSection(string stage)
    {
        var full = LoadFullPrompt();
        var startMarker = $"===STAGE:{stage}===";
        var start = full.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Pipeline prompt missing section {startMarker}.");
        }

        start += startMarker.Length;
        var next = full.IndexOf("===STAGE:", start, StringComparison.Ordinal);
        var section = next < 0 ? full[start..] : full[start..next];
        return section.Trim();
    }

    private static string LoadFullPrompt()
    {
        var assembly = typeof(PromptLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, "Prompts", "pipeline-system.txt");
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath);
        }

        throw new InvalidOperationException(
            $"Pipeline system prompt not found (embedded {ResourceName} or {filePath}).");
    }
}
