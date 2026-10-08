using System.Reflection;

namespace AgentHub.Agents.Healthy;

public static class PromptLoader
{
    private const string ResourceName = "AgentHub.Agents.Healthy.Prompts.healthy-system.txt";

    public static string LoadSystemPrompt()
    {
        var assembly = typeof(PromptLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }

        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "Prompts",
            "healthy-system.txt");
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath).Trim();
        }

        throw new InvalidOperationException(
            $"HEALTHY system prompt not found (embedded {ResourceName} or {filePath}).");
    }
}
