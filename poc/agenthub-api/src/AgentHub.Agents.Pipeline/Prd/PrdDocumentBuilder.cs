using System.Text.Json;
using AgentHub.Agents.Pipeline.Ia;
using AgentHub.Core.Models;

namespace AgentHub.Agents.Pipeline.Prd;

/// <summary>
/// Product-stage document builders for the POC pipeline (fallback when the LLM
/// does not return valid JSON). Mirrors deliveryMode-aware intake / Manual QA contracts.
/// </summary>
public static class PrdDocumentBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string BuildJson(TaskMessage message)
    {
        var delivery = DeliveryModeClassifier.Classify(
            message.Context.UserRequest,
            message.Context.ProjectMode,
            message.Context.BaselineArtifacts);
        var stories = delivery switch
        {
            DeliveryMode.Rewrite => new[]
            {
                new
                {
                    id = "S1",
                    title = "Greenfield rewrite to net10",
                    acceptance = new[]
                    {
                        "Baseline ingested from baselineSourcePath (or default fixture)",
                        "Target framework is net10.0",
                        "Preserved public APIs still pass smoke (/health and primary route)",
                        "code-state from graphify extract . --code-only after review"
                    }
                }
            },
            DeliveryMode.FeatureIncrement => new[]
            {
                new
                {
                    id = "S1",
                    title = "Feature increment on existing project",
                    acceptance = new[]
                    {
                        "Prior project workspace reused (not wiped)",
                        "New feature route works",
                        "Prior primary behaviour still works where applicable",
                        "code-state from graphify after review"
                    }
                }
            },
            _ => new[]
            {
                new
                {
                    id = "S1",
                    title = "Primary behaviour",
                    acceptance = new[] { "Happy path works locally", "Request-driven scaffold matches the ask" }
                }
            }
        };

        return JsonSerializer.Serialize(
            new
            {
                title = message.Context.ProjectName ?? "App",
                summary = message.Context.UserRequest,
                stories,
                stackProfile = message.Context.StackProfile,
                projectMode = message.Context.ProjectMode ?? "New",
                deliveryMode = delivery.ToString(),
                targetFramework = GreenfieldRewriteBuilder.TargetFramework,
                baselineSourcePath = DeliveryModeClassifier.ResolveBaselinePath(message.Context.BaselineArtifacts)
            },
            JsonOptions);
    }

    public static string BuildMarkdown(TaskMessage message, string prdJson) =>
        $"# {message.Context.ProjectName ?? "PRD"}\n\n{message.Context.UserRequest}\n\n```json\n{prdJson}\n```\n";
}
