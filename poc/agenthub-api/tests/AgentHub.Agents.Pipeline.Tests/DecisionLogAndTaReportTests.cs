using System.Text.Json;
using AgentHub.Agents.Pipeline.Artifacts;
using AgentHub.Agents.Pipeline.Ta;
using AgentHub.Core.Abstractions;
using Xunit;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class DecisionLogAndTaReportTests
{
    [Fact]
    public async Task Decision_log_appends_and_summarizes()
    {
        var store = new MemBlobs();
        await DecisionLogWriter.AppendAsync(
            store,
            "p",
            "w",
            1,
            "p/w/v1/",
            "PRD",
            "PRD",
            "persist",
            "Drafted PRD",
            ["Acceptance locked"],
            cancellationToken: CancellationToken.None);
        await DecisionLogWriter.AppendAsync(
            store,
            "p",
            "w",
            1,
            "p/w/v1/",
            "TA",
            "TA_REPORT",
            "verification-report",
            "Verification green",
            ["Ready for deploy"],
            cancellationToken: CancellationToken.None);

        var latest = await DecisionLogWriter.TryLoadLatestAsync(store, "p", "w", "p/w/v1/", CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(latest));
        using var doc = JsonDocument.Parse(latest!);
        Assert.Equal(2, doc.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Contains("PRD/PRD", doc.RootElement.GetProperty("decisionSummary").GetString(), StringComparison.Ordinal);
        Assert.True(store.Paths.ContainsKey("p/w/DECISION_LOG.json"));
        Assert.True(store.Paths.ContainsKey("p/w/DECISION_LOG_V1.json"));
    }

    [Fact]
    public void Verification_report_ready_for_deployment()
    {
        var json = TaVerificationReportBuilder.BuildJson(
            "http://127.0.0.1:9",
            "Plan: health + add",
            """{"status":"green","results":[{"name":"Add","ok":true}]}""",
            """{"status":"green","readyToMerge":true}""",
            "PRD → IA → TA",
            1);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("readyForDeployment").GetBoolean());
        Assert.Equal("green", doc.RootElement.GetProperty("status").GetString());
        var md = TaVerificationReportBuilder.BuildMarkdown("http://127.0.0.1:9", "green", "Plan", "sum");
        Assert.Contains("verification report", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEPLOY_SANDBOX", md, StringComparison.Ordinal);
    }

    private sealed class MemBlobs : IArtifactBlobStore
    {
        public Dictionary<string, string> Paths { get; } = new(StringComparer.Ordinal);

        public Task WriteJsonAsync(string path, string json, CancellationToken cancellationToken = default)
        {
            Paths[path] = json;
            return Task.CompletedTask;
        }

        public Task<string?> TryReadJsonAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Paths.TryGetValue(path, out var v) ? v : null);
    }
}
