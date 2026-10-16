using System.Text.Json;

namespace AgentHub.Agents.Pipeline.Ta;

/// <summary>
/// Structured TA verification report written after a green verify pass and before
/// DEPLOY_SANDBOX. Deployment (and the UI) can show plan → evidence → pass without
/// re-reading raw ta-results.
/// </summary>
public static class TaVerificationReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string BuildJson(
        string deployedUrl,
        string? testPlanText,
        string? taResultsJson,
        string? taStatusJson,
        string? decisionSummary,
        int revision)
    {
        object? results = null;
        var status = "green";
        try
        {
            if (!string.IsNullOrWhiteSpace(taResultsJson))
            {
                using var doc = JsonDocument.Parse(taResultsJson);
                results = JsonSerializer.Deserialize<object>(taResultsJson);
                if (doc.RootElement.TryGetProperty("status", out var st))
                {
                    status = st.GetString() ?? status;
                }
            }
        }
        catch (JsonException)
        {
            results = taResultsJson;
        }

        object? statusObj = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(taStatusJson))
            {
                statusObj = JsonSerializer.Deserialize<object>(taStatusJson);
            }
        }
        catch (JsonException)
        {
            statusObj = taStatusJson;
        }

        return JsonSerializer.Serialize(
            new
            {
                kind = "TaVerificationReport",
                readyForDeployment = string.Equals(status, "green", StringComparison.OrdinalIgnoreCase),
                status,
                revision,
                deployedUrl,
                at = DateTimeOffset.UtcNow,
                testPlanSummary = Truncate(testPlanText ?? "HTTP smoke + Playwright FE against deployed URL.", 2000),
                verification = results,
                taStatus = statusObj,
                decisionSummary = decisionSummary ?? "",
                handoffToDeployment =
                    "Testing Agent completed verification of the Implementation Agent's deployed app. Sandbox URL is ready for human acceptance."
            },
            JsonOptions);
    }

    public static string BuildMarkdown(
        string deployedUrl,
        string status,
        string? testPlanText,
        string? decisionSummary)
    {
        return
            "# Testing Agent — verification report\n\n"
            + $"**Status:** {status}\n\n"
            + $"**Deployed URL:** {deployedUrl}\n\n"
            + "## Test plan (what we verified)\n\n"
            + (string.IsNullOrWhiteSpace(testPlanText)
                ? "HTTP smoke (/health + primary API) and Playwright FE primary action.\n"
                : testPlanText.Trim() + "\n")
            + "\n## Decision summary (pipeline so far)\n\n"
            + (string.IsNullOrWhiteSpace(decisionSummary) ? "(none)\n" : decisionSummary.Trim() + "\n")
            + "\n## Handoff\n\n"
            + "Verification passed. Next stage is DEPLOY_SANDBOX (share URL; wait for approval).\n";
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max
            ? value
            : value[..(max - 14)] + "...[truncated]";
}
