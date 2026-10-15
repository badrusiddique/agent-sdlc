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
            // Map IA stages onto closest legacy section when missing.
            var fallback = stage switch
            {
                PipelineAgentConstants.StageIaPlan => PipelineAgentConstants.StageSystemDesign,
                PipelineAgentConstants.StageIaCodegen => PipelineAgentConstants.StageImplementation,
                PipelineAgentConstants.StageIaReview => PipelineAgentConstants.StageTesting,
                PipelineAgentConstants.StageIaPushDeploy => PipelineAgentConstants.StageDeployDev,
                _ => null
            };
            if (fallback is not null)
            {
                return LoadStageSection(fallback);
            }

            throw new InvalidOperationException($"Pipeline prompt missing section {startMarker}.");
        }

        start += startMarker.Length;
        var next = full.IndexOf("===STAGE:", start, StringComparison.Ordinal);
        var section = next < 0 ? full[start..] : full[start..next];
        return section.Trim();
    }

    public static string LoadIaSystemPrompt() =>
        LoadNamedPrompt("ia-system.txt", "You are the BuildGenix Implementation Agent.");

    public static string LoadTaSystemPrompt() =>
        LoadNamedPrompt("ta-system.txt", "You are the BuildGenix Testing Agent.");

    public static string LoadIaPhasePrompt(string phaseFile) =>
        LoadNamedPrompt(phaseFile, $"Phase instructions for {phaseFile}.");

    public static string LoadNamedPromptFile(string fileName) =>
        LoadNamedPrompt(fileName, $"Phase instructions for {fileName}.");

    private static string LoadNamedPrompt(string fileName, string fallback)
    {
        var assembly = typeof(PromptLoader).Assembly;
        var resource = $"AgentHub.Agents.Pipeline.Prompts.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Trim();
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, "Prompts", fileName);
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath).Trim();
        }

        return fallback;
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
