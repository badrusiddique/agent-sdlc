using System.Text.Json;
using AgentHub.Agents.Pipeline.Artifacts;
using AgentHub.Agents.Pipeline.Deploy;
using AgentHub.Agents.Pipeline.Ta;
using AgentHub.Core.Models;
using Xunit;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class TaConcernAndPlaywrightTests
{
    [Fact]
    public void Concern_package_marks_routeback()
    {
        var json = TaConcernPackage.Build(
            "http://127.0.0.1:9",
            """{"status":"red","results":[{"name":"Add","ok":false}]}""",
            ["Add"],
            1);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("RouteBack", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("IA_CODEGEN", doc.RootElement.GetProperty("routeBackTo").GetString());
    }

    [Fact]
    public void Playwright_suite_writes_spec()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-pw-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cases = DeployedSmokeGenerator.DefaultCases(
                "fe-be",
                "Create a calculator app that adds two numbers",
                "Calc",
                "New",
                null);
            PlaywrightSuiteGenerator.WriteSuite(root, cases, "Create a calculator", "fe-be", "Calc");
            Assert.True(File.Exists(Path.Combine(root, "tests", "e2e", "playwright", "fe.spec.js")));
            Assert.True(File.Exists(Path.Combine(root, "tests", "e2e", "playwright", "run-playwright.sh")));
            var spec = File.ReadAllText(Path.Combine(root, "tests", "e2e", "playwright", "fe.spec.js"));
            Assert.Contains("#go", spec, StringComparison.Ordinal);
            Assert.Contains("page.goto", spec, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Versioned_paths_are_stable()
    {
        Assert.Equal("p/w/v2/", VersionedArtifactPaths.RevisionPrefix("p", "w", 2));
        Assert.Equal("p/w/PRD_V1.json", VersionedArtifactPaths.NamedVersion("p", "w", "PRD", 1));
        Assert.Equal("p/w/LATEST.json", VersionedArtifactPaths.LatestPointer("p", "w"));
        Assert.Equal("PRD", VersionedArtifactPaths.LogicalStem("prd"));
        Assert.Equal("TA_CONCERNS", VersionedArtifactPaths.LogicalStem("taConcerns"));
    }

    [Fact]
    public void Sandbox_publisher_includes_url()
    {
        var msg = new TaskMessage
        {
            ProjectId = "p",
            WorkItemId = "w",
            OutputRevision = 2,
            Context = new TaskMessageContext { DeployedUrl = "http://127.0.0.1:1" }
        };
        var json = SandboxDeployPublisher.BuildArtifact(msg, "http://127.0.0.1:1", null);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("sandbox", doc.RootElement.GetProperty("environment").GetString());
        Assert.Equal("waiting_for_approval", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("http://127.0.0.1:1", doc.RootElement.GetProperty("deployedUrl").GetString());
    }
}
