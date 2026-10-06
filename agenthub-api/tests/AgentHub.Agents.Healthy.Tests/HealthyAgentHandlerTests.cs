using AgentHub.Agents.Healthy;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.Agents.Healthy.Tests;

public sealed class HealthyAgentHandlerTests
{
    [Fact]
    public async Task RunAsync_writes_blob_commits_ledger_and_returns_Completed()
    {
        var llm = new FakeLlm();
        var blobs = new FakeBlobStore();
        var ledger = new InMemoryEffectLedger();
        var activity = new InMemoryActivityLog();
        var handler = new HealthyAgentHandler(
            llm,
            blobs,
            ledger,
            activity,
            NullLogger<HealthyAgentHandler>.Instance,
            deliberatePause: TimeSpan.Zero);

        var message = new TaskMessage
        {
            WorkItemId = "wi1",
            ProjectId = "p1",
            TaskId = "t1",
            CorrelationId = "c1",
            InputVersion = 1,
            Stage = HealthyAgentConstants.StageCode
        };

        var result = await handler.RunAsync(message, deliveryCount: 1);

        Assert.Equal(AgentResultStatus.Completed, result.Status);
        Assert.Equal("p1/wi1/v1/healthy-result.json", result.Artifacts[HealthyAgentConstants.ArtifactKey]);
        Assert.Equal("p1:wi1", result.StateContextId);
        Assert.Contains(blobs.Writes, w => w.Path == "p1/wi1/v1/healthy-result.json");
        Assert.True(ledger.Has(HealthyAgentConstants.IntentLlm, 100));
        Assert.True(ledger.Has(HealthyAgentConstants.IntentArtifact, 200));
        Assert.Contains(activity.Entries, e => e.Message.Contains("HEALTHY started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_skips_llm_on_ledger_hit()
    {
        var llm = new FakeLlm();
        var blobs = new FakeBlobStore();
        var ledger = new InMemoryEffectLedger();
        await ledger.CommitAsync(new EffectIntent(
            "wi1",
            "t1",
            HealthyAgentConstants.IntentLlm,
            100,
            null,
            """{"text":"cached","modelId":"fixture","inputTokens":1,"outputTokens":1}"""));
        var handler = new HealthyAgentHandler(
            llm,
            blobs,
            ledger,
            new InMemoryActivityLog(),
            NullLogger<HealthyAgentHandler>.Instance,
            TimeSpan.Zero);

        var message = new TaskMessage
        {
            WorkItemId = "wi1",
            ProjectId = "p1",
            TaskId = "t1",
            CorrelationId = "c1",
            InputVersion = 2,
            Stage = HealthyAgentConstants.StageCode
        };

        await handler.RunAsync(message, 1);
        Assert.Equal(0, llm.Calls);
        Assert.Single(blobs.Writes);
    }

    private sealed class FakeLlm : ILlmClient
    {
        public string ProviderName => "Fixture";
        public int Calls { get; private set; }

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new LlmCompletionResult("joke", "fixture", 1, 2));
        }
    }

    private sealed class FakeBlobStore : IArtifactBlobStore
    {
        public List<(string Path, string Body)> Writes { get; } = [];

        public Task WriteJsonAsync(string blobPath, string jsonBody, CancellationToken cancellationToken = default)
        {
            Writes.Add((blobPath, jsonBody));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryEffectLedger : IEffectLedger
    {
        private readonly Dictionary<string, CommittedEffect> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Intent, int Ordinal)> _meta = new(StringComparer.Ordinal);

        public Task<CommittedEffect?> TryGetCommittedAsync(
            string workItemId,
            string taskId,
            string intentKind,
            int ordinal,
            CancellationToken cancellationToken = default)
        {
            var key = $"{taskId}_{intentKind}_{ordinal:D6}";
            return Task.FromResult(_rows.TryGetValue(key, out var e) ? e : null);
        }

        public bool Has(string intent, int ordinal) =>
            _meta.Values.Any(r => r.Intent == intent && r.Ordinal == ordinal);

        public Task<EffectCommitResult> CommitAsync(
            EffectIntent intent,
            CancellationToken cancellationToken = default)
        {
            var key = $"{intent.TaskId}_{intent.IntentKind}_{intent.Ordinal:D6}";
            if (_rows.TryGetValue(key, out var existing))
            {
                return Task.FromResult(new EffectCommitResult(false, existing));
            }

            var effect = new CommittedEffect(intent.InputJson, intent.OutputJson);
            _rows[key] = effect;
            _meta[key] = (intent.IntentKind, intent.Ordinal);
            return Task.FromResult(new EffectCommitResult(true, effect));
        }
    }

    private sealed class InMemoryActivityLog : IActivityLog
    {
        public List<(string Message, string Level)> Entries { get; } = [];

        public Task AppendAsync(
            string workItemId,
            string taskId,
            string stage,
            string level,
            string message,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            Entries.Add((message, level));
            return Task.CompletedTask;
        }
    }
}
