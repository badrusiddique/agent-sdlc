using System.Text.Json;
using AgentHub.Agents.Pipeline.Prd;
using AgentHub.Agents.Pipeline.Sd;
using AgentHub.Core.Models;
using Xunit;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class PrdSdDocumentBuilderTests
{
    [Fact]
    public void Prd_basic_calculator_has_delivery_mode_basic()
    {
        var json = PrdDocumentBuilder.BuildJson(Msg(
            "Create a calculator app that adds two numbers",
            "New",
            "Calc",
            null));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Basic", doc.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Equal("net10.0", doc.RootElement.GetProperty("targetFramework").GetString());
        Assert.Equal("New", doc.RootElement.GetProperty("projectMode").GetString());
    }

    [Fact]
    public void Prd_rewrite_includes_baseline_path()
    {
        var json = PrdDocumentBuilder.BuildJson(Msg(
            "Rewrite the existing net8 calculator as net10",
            "Rewrite",
            "RewriteApp",
            new Dictionary<string, string> { ["baselineSourcePath"] = "fixtures/legacy-net8-calculator" }));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Rewrite", doc.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Equal(
            "fixtures/legacy-net8-calculator",
            doc.RootElement.GetProperty("baselineSourcePath").GetString());
        var md = PrdDocumentBuilder.BuildMarkdown(Msg("r", "Rewrite", "R", null), json);
        Assert.Contains("```json", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Prd_feature_increment_on_existing()
    {
        var json = PrdDocumentBuilder.BuildJson(Msg(
            "Update the calculator app to subtract two numbers",
            "Existing",
            "Multi",
            null));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("FeatureIncrement", doc.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Equal("Existing", doc.RootElement.GetProperty("projectMode").GetString());
    }

    [Fact]
    public void TechSpec_basic_todo_route()
    {
        var json = TechSpecBuilder.BuildJson(Msg("Create a todo list app", "New", "Todo", null));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Basic", doc.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Equal("Todo", doc.RootElement.GetProperty("appKind").GetString());
        var path = doc.RootElement.GetProperty("apis")[0].GetProperty("path").GetString();
        Assert.Equal("/api/todos", path);
    }

    [Fact]
    public void TechSpec_rewrite_preserves_add()
    {
        var json = TechSpecBuilder.BuildJson(Msg(
            "Rewrite the existing net8 calculator as net10",
            "Rewrite",
            "R",
            new Dictionary<string, string> { ["baselineSourcePath"] = "fixtures/legacy-net8-calculator" }));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Rewrite", doc.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Equal("/api/add", doc.RootElement.GetProperty("apis")[0].GetProperty("path").GetString());
        var md = TechSpecBuilder.BuildMarkdown(Msg("r", "Rewrite", "R", null), json);
        Assert.Contains("System design", md, StringComparison.Ordinal);
    }

    [Fact]
    public void TechSpec_feature_subtract_route()
    {
        var json = TechSpecBuilder.BuildJson(Msg(
            "Update the calculator app to subtract two numbers",
            "Existing",
            "Multi",
            null));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("FeatureIncrement", doc.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Equal("/api/subtract", doc.RootElement.GetProperty("apis")[0].GetProperty("path").GetString());
    }

    private static TaskMessage Msg(
        string request,
        string mode,
        string name,
        Dictionary<string, string>? baseline) =>
        new()
        {
            ProjectId = "p1",
            WorkItemId = "w1",
            TaskId = "t1",
            Stage = "PRD",
            CorrelationId = "c1",
            Context = new TaskMessageContext
            {
                UserRequest = request,
                ProjectMode = mode,
                ProjectName = name,
                StackProfile = "fe-be",
                BaselineArtifacts = baseline ?? new Dictionary<string, string>(StringComparer.Ordinal)
            }
        };
}
