using AgentHub.Agents.Healthy;
using AgentHub.Agents.Healthy.Abstractions;
using AgentHub.Agents.Healthy.Models;
using AgentHub.Data.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.Agents.Healthy.Tests;

public sealed class HealthyAgentDispatcherTests
{
    [Fact]
    public async Task ProcessAsync_publish_then_mark_order_is_D_C1()
    {
        var order = new List<string>();
        var results = new FakeResults(onPublish: () => order.Add("publish"));
        var dedup = new FakeDedup(onMark: () => order.Add("mark"));
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: false);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t1","correlationId":"c","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t1:1", deliveryCount: 1);

        Assert.Equal(HealthyDispatchKind.Handled, outcome.Kind);
        Assert.Equal(new[] { "publish", "mark" }, order);
        Assert.Equal(AgentResultStatus.Completed, outcome.Result!.Status);
    }

    [Fact]
    public async Task ProcessAsync_already_processed_skips_handler()
    {
        var results = new FakeResults();
        var dedup = new FakeDedup { Already = true };
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: false);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t1","correlationId":"c","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t1:1", 1);

        Assert.Equal(HealthyDispatchKind.AlreadyProcessed, outcome.Kind);
        Assert.Empty(results.Published);
        Assert.Empty(dedup.Marked);
    }

    [Fact]
    public async Task ProcessAsync_malformed_dead_letters_without_mark()
    {
        var results = new FakeResults();
        var dedup = new FakeDedup();
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: false);

        var outcome = await dispatcher.ProcessAsync("{not-json", "x", 1);

        Assert.Equal(HealthyDispatchKind.DeadLetter, outcome.Kind);
        Assert.Empty(results.Published);
        Assert.Empty(dedup.Marked);
    }

    [Fact]
    public async Task ProcessAsync_handler_failure_still_publishes_Failed_then_marks()
    {
        var order = new List<string>();
        var results = new FakeResults(onPublish: () => order.Add("publish"));
        var dedup = new FakeDedup(onMark: () => order.Add("mark"));
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: true);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t1","correlationId":"c","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t1:1", 1);

        Assert.Equal(HealthyDispatchKind.Handled, outcome.Kind);
        Assert.Equal(AgentResultStatus.Failed, outcome.Result!.Status);
        Assert.Equal(new[] { "publish", "mark" }, order);
    }

    [Fact]
    public void LogicalMessageId_and_StateContextId_match_D_I2_4()
    {
        Assert.Equal("task:3", HealthyAgentConstants.LogicalMessageId("task", 3));
        Assert.Equal("proj:wi", HealthyAgentConstants.StateContextId("proj", "wi"));
        Assert.Equal(
            "proj/wi/v2/healthy-result.json",
            HealthyAgentConstants.BlobPath("proj", "wi", 2));
    }

    private static HealthyAgentDispatcher CreateDispatcher(
        FakeResults results,
        FakeDedup dedup,
        bool handlerThrows)
    {
        var handler = new HealthyAgentHandler(
            new ThrowingOrOkLlm(handlerThrows),
            new NoopBlob(),
            new EmptyLedger(),
            new NoopActivity(),
            NullLogger<HealthyAgentHandler>.Instance,
            TimeSpan.Zero);

        return new HealthyAgentDispatcher(
            handler,
            results,
            dedup,
            NullLogger<HealthyAgentDispatcher>.Instance);
    }

    private sealed class ThrowingOrOkLlm : ILlmClient
    {
        private readonly bool _throw;

        public ThrowingOrOkLlm(bool @throw) => _throw = @throw;

        public string ProviderName => "Fixture";

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_throw)
            {
                throw new InvalidOperationException("forced");
            }

            return Task.FromResult(new LlmCompletionResult("ok", "fixture", 1, 1));
        }
    }

    private sealed class NoopBlob : IArtifactBlobStore
    {
        public Task WriteJsonAsync(string blobPath, string jsonBody, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyLedger : IEffectLedgerRepository
    {
        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<EffectLedgerCommitResult> CommitAsync(
            AgentHub.Data.Entities.EffectLedgerEntity entry,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectLedgerCommitResult(true, entry));

        public Task<AgentHub.Data.Entities.EffectLedgerEntity?> TryGetCommittedAsync(
            string workItemId,
            string taskId,
            string intentKind,
            int ordinal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentHub.Data.Entities.EffectLedgerEntity?>(null);
    }

    private sealed class NoopActivity : IActivityLogRepository
    {
        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AppendAsync(
            AgentHub.Data.Entities.ActivityLogEntity entry,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeResults : IAgentResultPublisher
    {
        private readonly Action? _onPublish;

        public FakeResults(Action? onPublish = null) => _onPublish = onPublish;

        public List<AgentResult> Published { get; } = [];

        public Task PublishResultAsync(AgentResult result, CancellationToken cancellationToken = default)
        {
            _onPublish?.Invoke();
            Published.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDedup : IAgProcessedEventsRepository
    {
        private readonly Action? _onMark;

        public FakeDedup(Action? onMark = null) => _onMark = onMark;

        public bool Already { get; init; }
        public List<string> Marked { get; } = [];

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Already || Marked.Contains(messageId));

        public Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken cancellationToken = default)
        {
            _onMark?.Invoke();
            if (Already)
            {
                return Task.FromResult(false);
            }

            Marked.Add(messageId);
            return Task.FromResult(true);
        }
    }
}
