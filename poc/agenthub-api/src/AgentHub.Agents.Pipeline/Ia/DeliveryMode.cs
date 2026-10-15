using System.Text.RegularExpressions;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// How IA materializes the work-item workspace. Keeps basic scaffolds, greenfield
/// rewrite of an existing baseline, and multi-WI feature increments distinct.
/// </summary>
public enum DeliveryMode
{
    /// <summary>New app from a closed request-driven scaffold (hello/todo/calc/…).</summary>
    Basic,

    /// <summary>Ingest an existing baseline repo/folder and rewrite it as net10 greenfield.</summary>
    Rewrite,

    /// <summary>Build on a prior work-item workspace for the same project (feature WI).</summary>
    FeatureIncrement
}

public static class DeliveryModeClassifier
{
    public static DeliveryMode Classify(
        string? userRequest,
        string? projectMode,
        IReadOnlyDictionary<string, string>? baselineArtifacts = null)
    {
        var mode = projectMode?.Trim() ?? "New";
        var text = (userRequest ?? "").ToLowerInvariant();
        var hasBaselinePath = baselineArtifacts is not null
            && (baselineArtifacts.ContainsKey("baselineSourcePath")
                || baselineArtifacts.ContainsKey("sourceRepo")
                || baselineArtifacts.ContainsKey("baselineRoot"));

        if (mode.Equals("Rewrite", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("GreenfieldRewrite", StringComparison.OrdinalIgnoreCase)
            || hasBaselinePath
                && (text.Contains("rewrite", StringComparison.Ordinal)
                    || text.Contains("migrate", StringComparison.Ordinal)
                    || text.Contains("upgrade", StringComparison.Ordinal)
                    || Regex.IsMatch(text, @"\.?\s*net\s*8")
                    || text.Contains("newer version", StringComparison.Ordinal)
                    || text.Contains("greenfield", StringComparison.Ordinal)
                        && text.Contains("existing", StringComparison.Ordinal)))
        {
            return DeliveryMode.Rewrite;
        }

        if (mode.Equals("Existing", StringComparison.OrdinalIgnoreCase)
            || text.Contains("update the", StringComparison.Ordinal)
            || text.Contains("add feature", StringComparison.Ordinal)
            || text.Contains("build on", StringComparison.Ordinal)
            || (text.Contains("subtract", StringComparison.Ordinal) && text.Contains("calculator", StringComparison.Ordinal)))
        {
            return DeliveryMode.FeatureIncrement;
        }

        // Request-only rewrite cues without explicit baseline still count when wording is clear.
        if (text.Contains("rewrite", StringComparison.Ordinal)
            || text.Contains("migrate to .net", StringComparison.Ordinal)
            || text.Contains("upgrade to .net 10", StringComparison.Ordinal))
        {
            return DeliveryMode.Rewrite;
        }

        return DeliveryMode.Basic;
    }

    public static string? ResolveBaselinePath(IReadOnlyDictionary<string, string>? baselineArtifacts)
    {
        if (baselineArtifacts is null)
        {
            return null;
        }

        foreach (var key in new[] { "baselineSourcePath", "sourceRepo", "baselineRoot" })
        {
            if (baselineArtifacts.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                return path.Trim();
            }
        }

        return null;
    }
}
