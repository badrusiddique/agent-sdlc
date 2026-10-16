using System.Text.Json;
using AgentHub.Agents.Pipeline.Ia;
using AgentHub.Core.Models;

namespace AgentHub.Agents.Pipeline.Sd;

/// <summary>
/// System Design stage builders for the POC pipeline (fallback techspec.json when
/// the LLM does not return valid JSON). Keeps deliveryMode aligned with PRD / IA.
/// </summary>
public static class TechSpecBuilder
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
        var kind = RequestDrivenCodegen.Classify(
            message.Context.UserRequest,
            message.Context.StackProfile,
            message.Context.ProjectName);
        object api = delivery switch
        {
            DeliveryMode.Rewrite =>
                new { method = "GET", path = "/api/add", purpose = "preserved after net10 rewrite" },
            DeliveryMode.FeatureIncrement when FeatureIncrementBuilder.ClassifyFeature(message.Context.UserRequest)
                == FeatureIncrementBuilder.FeatureKind.Subtract =>
                new { method = "GET", path = "/api/subtract", purpose = "feature increment" },
            DeliveryMode.FeatureIncrement when FeatureIncrementBuilder.ClassifyFeature(message.Context.UserRequest)
                == FeatureIncrementBuilder.FeatureKind.Multiply =>
                new { method = "GET", path = "/api/multiply", purpose = "feature increment" },
            _ => kind switch
            {
                RequestDrivenCodegen.AppKind.Calculator =>
                    new { method = "GET", path = "/api/add", purpose = "add two numbers" },
                RequestDrivenCodegen.AppKind.Todo =>
                    new { method = "GET", path = "/api/todos", purpose = "list todos" },
                RequestDrivenCodegen.AppKind.HelloLlm =>
                    new { method = "GET", path = "/api/thought", purpose = "thought of the day" },
                _ => new { method = "GET", path = "/api/hello", purpose = "hello" }
            }
        };
        return JsonSerializer.Serialize(
            new
            {
                components = new[] { "fe", "be" },
                apis = new object[] { api },
                phases = new[]
                {
                    new
                    {
                        id = 1,
                        name = delivery == DeliveryMode.Rewrite ? "Rewrite baseline" : "Scaffold and run"
                    }
                },
                deploy = "local-compose",
                stackProfile = message.Context.StackProfile,
                deliveryMode = delivery.ToString(),
                targetFramework = GreenfieldRewriteBuilder.TargetFramework,
                appKind = kind.ToString()
            },
            JsonOptions);
    }

    public static string BuildMarkdown(TaskMessage message, string techJson) =>
        $"# System design\n\nStack: {message.Context.StackProfile}\n\n```json\n{techJson}\n```\n";
}
