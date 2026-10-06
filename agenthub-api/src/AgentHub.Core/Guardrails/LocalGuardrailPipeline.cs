using System.Text.RegularExpressions;

namespace AgentHub.Core.Guardrails;

/// <summary>
/// Always-on deterministic guardrails (Phase 0). Azure Content Safety Prompt Shields
/// plugs in later behind the same <see cref="IGuardrailPipeline"/> when configured.
/// Inspired by check catalogs such as GuardrailsAI Hub; does not depend on that runtime.
/// </summary>
public sealed partial class LocalGuardrailPipeline : IGuardrailPipeline
{
    public const int DefaultMaxInputChars = 8000;
    public const int DefaultMaxOutputChars = 4000;

    private static readonly string[] InjectionPhrases =
    [
        "ignore all previous instructions",
        "ignore previous instructions",
        "disregard your rules",
        "disregard all rules",
        "you are dan",
        "do anything now",
        "jailbreak",
        "reveal your system prompt",
        "show your system prompt",
        "bypass safety",
        "act without restrictions"
    ];

    public GuardrailVerdict EvaluateInput(string userOrDocumentText, string? correlationId)
    {
        var corr = correlationId ?? string.Empty;
        var text = userOrDocumentText ?? string.Empty;
        var reasons = new List<string>();

        if (text.Length > DefaultMaxInputChars)
        {
            reasons.Add(GuardrailReasonCodes.InputTooLong);
        }

        if (ContainsInjectionPhrase(text))
        {
            reasons.Add(GuardrailReasonCodes.PromptInjectionSuspected);
        }

        if (ContainsSecretShape(text))
        {
            reasons.Add(GuardrailReasonCodes.SecretSuspected);
        }

        if (reasons.Count > 0)
        {
            return new GuardrailVerdict(GuardrailDecision.Block, reasons, corr, "input");
        }

        return new GuardrailVerdict(
            GuardrailDecision.Allow,
            [GuardrailReasonCodes.InputOk],
            corr,
            "input");
    }

    public GuardrailVerdict EvaluateOutput(string modelText, string systemPrompt, string? correlationId)
    {
        var corr = correlationId ?? string.Empty;
        var text = modelText ?? string.Empty;
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            reasons.Add(GuardrailReasonCodes.OutputEmpty);
        }

        if (text.Length > DefaultMaxOutputChars)
        {
            reasons.Add(GuardrailReasonCodes.OutputTooLong);
        }

        if (HasMarkdownHeading(text))
        {
            reasons.Add(GuardrailReasonCodes.OutputShapeViolation);
        }

        if (ContainsSecretShape(text))
        {
            reasons.Add(GuardrailReasonCodes.SecretSuspected);
        }

        if (LeaksSystemPrompt(text, systemPrompt))
        {
            reasons.Add(GuardrailReasonCodes.SystemPromptLeakage);
        }

        if (reasons.Count > 0)
        {
            return new GuardrailVerdict(GuardrailDecision.Block, reasons, corr, "output");
        }

        return new GuardrailVerdict(
            GuardrailDecision.Allow,
            [GuardrailReasonCodes.OutputOk],
            corr,
            "output");
    }

    private static bool ContainsInjectionPhrase(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var phrase in InjectionPhrases)
        {
            if (lower.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMarkdownHeading(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal)
                || trimmed.StartsWith("## ", StringComparison.Ordinal)
                || trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LeaksSystemPrompt(string output, string systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt) || systemPrompt.Trim().Length < 24)
        {
            return false;
        }

        var needle = systemPrompt.Trim();
        if (needle.Length > 80)
        {
            needle = needle[..80];
        }

        return output.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsSecretShape(string text) =>
        GitHubPat().IsMatch(text)
        || OpenAiSk().IsMatch(text)
        || AzureConnString().IsMatch(text)
        || GenericBearer().IsMatch(text);

    [GeneratedRegex(@"ghp_[A-Za-z0-9_]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubPat();

    [GeneratedRegex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiSk();

    [GeneratedRegex(
        @"DefaultEndpointsProtocol=https?;AccountKey=[A-Za-z0-9+/=]{16,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AzureConnString();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericBearer();
}
