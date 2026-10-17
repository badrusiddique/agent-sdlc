using System.Text;
using System.Text.Json;
using AgentHub.Agents.Pipeline.Ia;
using AgentHub.Core.Models;

namespace AgentHub.Agents.Pipeline.Sd;

/// <summary>
/// System Design stage builders for the POC pipeline (fallback techspec.json when
/// the LLM does not return valid JSON). Keeps deliveryMode aligned with PRD / IA.
/// Richer spoon-feed pack so IA/TA consume routes, phases, and acceptance checks.
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
        var apis = BuildApis(delivery, kind, message.Context.UserRequest);
        var primary = apis[0];
        var phases = delivery switch
        {
            DeliveryMode.Rewrite => new object[]
            {
                new { id = 1, name = "Ingest baseline", verify = "dotnet build" },
                new { id = 2, name = "Retarget net10.0", verify = "dotnet test" },
                new { id = 3, name = "Preserve public APIs", verify = "curl primary route" }
            },
            DeliveryMode.FeatureIncrement => new object[]
            {
                new { id = 1, name = "Clone prior workspace", verify = "workspace exists" },
                new { id = 2, name = "Add feature route + UI", verify = "dotnet test" },
                new { id = 3, name = "Smoke new API", verify = "curl new route" }
            },
            _ => new object[]
            {
                new { id = 1, name = "Scaffold FE+BE mono-repo", verify = "dotnet build" },
                new { id = 2, name = "Wire primary API + UI", verify = "dotnet test" },
                new { id = 3, name = "Compose up + smoke", verify = "curl primary route" }
            }
        };

        var monoFolder = $"project-{message.ProjectId}-work-item-{message.WorkItemId}";
        return JsonSerializer.Serialize(
            new
            {
                version = "SystemDesign_v1",
                components = new[] { "frontend", "backend" },
                monoRepo = new
                {
                    folder = monoFolder,
                    backend = "backend/",
                    frontend = "frontend/",
                    compose = "docker-compose.yml"
                },
                apis,
                primaryApi = primary,
                phases,
                acceptanceChecks = apis.Select(a => new
                {
                    method = a.method,
                    path = a.path,
                    expectStatus = 200,
                    retired = false
                }).ToArray(),
                filePlan = new[]
                {
                    $"{monoFolder}/backend/Program.cs",
                    $"{monoFolder}/frontend/index.html",
                    $"{monoFolder}/docker-compose.yml"
                },
                deploy = new
                {
                    mode = "local-compose",
                    smokePath = primary.path
                },
                stackProfile = message.Context.StackProfile,
                deliveryMode = delivery.ToString(),
                targetFramework = GreenfieldRewriteBuilder.TargetFramework,
                appKind = kind.ToString(),
                projectMode = message.Context.ProjectMode,
                workBranch = $"feature/project-{message.ProjectId}-work-item-{message.WorkItemId}"
            },
            JsonOptions);
    }

    public static string BuildMarkdown(TaskMessage message, string techJson)
    {
        using var doc = JsonDocument.Parse(techJson);
        var root = doc.RootElement;
        var delivery = root.TryGetProperty("deliveryMode", out var dm) ? dm.GetString() : "?";
        var appKind = root.TryGetProperty("appKind", out var ak) ? ak.GetString() : "?";
        var stack = message.Context.StackProfile ?? "fe-be";
        var sb = new StringBuilder();
        sb.AppendLine("# System design");
        sb.AppendLine();
        sb.AppendLine("## 0. Delivery");
        sb.AppendLine($"- **deliveryMode:** {delivery}");
        sb.AppendLine($"- **appKind:** {appKind}");
        sb.AppendLine($"- **stackProfile:** {stack}");
        sb.AppendLine($"- **targetFramework:** {GreenfieldRewriteBuilder.TargetFramework}");
        sb.AppendLine();
        sb.AppendLine("## 6. APIs (authoritative for IA/TA)");
        if (root.TryGetProperty("apis", out var apis) && apis.ValueKind == JsonValueKind.Array)
        {
            foreach (var api in apis.EnumerateArray())
            {
                var method = api.TryGetProperty("method", out var m) ? m.GetString() : "GET";
                var path = api.TryGetProperty("path", out var p) ? p.GetString() : "/";
                var purpose = api.TryGetProperty("purpose", out var u) ? u.GetString() : "";
                sb.AppendLine($"- `{method} {path}` — {purpose}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Phases");
        if (root.TryGetProperty("phases", out var phases) && phases.ValueKind == JsonValueKind.Array)
        {
            foreach (var phase in phases.EnumerateArray())
            {
                var id = phase.TryGetProperty("id", out var i) ? i.ToString() : "?";
                var name = phase.TryGetProperty("name", out var n) ? n.GetString() : "";
                var verify = phase.TryGetProperty("verify", out var v) ? v.GetString() : "";
                sb.AppendLine($"- Phase {id}: {name} (verify: {verify})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Acceptance checks (TA)");
        if (root.TryGetProperty("acceptanceChecks", out var checks) && checks.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in checks.EnumerateArray())
            {
                var method = c.TryGetProperty("method", out var m) ? m.GetString() : "GET";
                var path = c.TryGetProperty("path", out var p) ? p.GetString() : "/";
                sb.AppendLine($"- `{method} {path}` expect 200 (retired=false)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Machine-readable techspec");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(techJson);
        sb.AppendLine("```");
        return sb.ToString();
    }

    /// <summary>
    /// Prefer techspec appKind / primary API when IA materializes Basic scaffolds.
    /// </summary>
    public static bool TryReadSpoonFeed(
        string? techJson,
        out RequestDrivenCodegen.AppKind? appKind,
        out string? primaryPath)
    {
        appKind = null;
        primaryPath = null;
        if (string.IsNullOrWhiteSpace(techJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(techJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("appKind", out var ak)
                && Enum.TryParse<RequestDrivenCodegen.AppKind>(ak.GetString(), ignoreCase: true, out var kind))
            {
                appKind = kind;
            }

            if (root.TryGetProperty("primaryApi", out var primary)
                && primary.TryGetProperty("path", out var pathEl))
            {
                primaryPath = pathEl.GetString();
            }
            else if (root.TryGetProperty("apis", out var apis)
                     && apis.ValueKind == JsonValueKind.Array
                     && apis.GetArrayLength() > 0
                     && apis[0].TryGetProperty("path", out var firstPath))
            {
                primaryPath = firstPath.GetString();
            }

            return appKind is not null || primaryPath is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ApiRow[] BuildApis(DeliveryMode delivery, RequestDrivenCodegen.AppKind kind, string? userRequest)
    {
        return delivery switch
        {
            DeliveryMode.Rewrite =>
            [
                new ApiRow("GET", "/api/add", "preserved after net10 rewrite")
            ],
            DeliveryMode.FeatureIncrement when FeatureIncrementBuilder.ClassifyFeature(userRequest)
                == FeatureIncrementBuilder.FeatureKind.Subtract =>
            [
                new ApiRow("GET", "/api/add", "prior add"),
                new ApiRow("GET", "/api/subtract", "feature increment")
            ],
            DeliveryMode.FeatureIncrement when FeatureIncrementBuilder.ClassifyFeature(userRequest)
                == FeatureIncrementBuilder.FeatureKind.Multiply =>
            [
                new ApiRow("GET", "/api/add", "prior add"),
                new ApiRow("GET", "/api/multiply", "feature increment")
            ],
            DeliveryMode.FeatureIncrement =>
            [
                new ApiRow("GET", "/api/add", "prior add"),
                new ApiRow("GET", "/api/feature", "feature increment")
            ],
            _ => kind switch
            {
                RequestDrivenCodegen.AppKind.Calculator =>
                [
                    new ApiRow("GET", "/api/add", "add two numbers"),
                    new ApiRow("GET", "/health", "liveness")
                ],
                RequestDrivenCodegen.AppKind.Todo =>
                [
                    new ApiRow("GET", "/api/todos", "list todos"),
                    new ApiRow("POST", "/api/todos", "create todo"),
                    new ApiRow("GET", "/health", "liveness")
                ],
                RequestDrivenCodegen.AppKind.HelloLlm =>
                [
                    new ApiRow("GET", "/api/thought", "thought of the day"),
                    new ApiRow("GET", "/api/hello", "hello"),
                    new ApiRow("GET", "/health", "liveness")
                ],
                RequestDrivenCodegen.AppKind.Hello =>
                [
                    new ApiRow("GET", "/api/hello", "hello"),
                    new ApiRow("GET", "/health", "liveness")
                ],
                _ =>
                [
                    new ApiRow("GET", "/api/hello", "hello"),
                    new ApiRow("GET", "/health", "liveness")
                ]
            }
        };
    }

    private sealed record ApiRow(string method, string path, string purpose);
}
