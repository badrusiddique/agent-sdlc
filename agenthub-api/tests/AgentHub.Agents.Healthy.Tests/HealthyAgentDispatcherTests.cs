using AgentHub.Agents.Healthy;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Models;
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
    public async Task ProcessAsync_handler_failure_before_max_delivery_abandons_without_mark()
    {
        var results = new FakeResults();
        var dedup = new FakeDedup();
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: true);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t1","correlationId":"c","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t1:1", deliveryCount: 1, maxDeliveryCount: 5);

        Assert.Equal(HealthyDispatchKind.Abandon, outcome.Kind);
        Assert.Empty(results.Published);
        Assert.Empty(dedup.Marked);
    }

    [Fact]
    public async Task ProcessAsync_handler_failure_on_last_delivery_publishes_Failed_marks_dead_letters()
    {
        var order = new List<string>();
        var results = new FakeResults(onPublish: () => order.Add("publish"));
        var dedup = new FakeDedup(onMark: () => order.Add("mark"));
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: true);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t1","correlationId":"c","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t1:1", deliveryCount: 5, maxDeliveryCount: 5);

        Assert.Equal(HealthyDispatchKind.DeadLetter, outcome.Kind);
        Assert.Equal(AgentResultStatus.Failed, outcome.Result!.Status);
        Assert.Equal(new[] { "publish", "mark" }, order);
    }

    [Fact]
    public async Task ProcessAsync_publish_failure_does_not_mark()
    {
        var dedup = new FakeDedup();
        var results = new FakeResults(onPublish: () => throw new InvalidOperationException("sb down"));
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: false);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t1","correlationId":"c","inputVersion":1}
            """;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.ProcessAsync(body, "t1:1", deliveryCount: 1, maxDeliveryCount: 5));
        Assert.Empty(dedup.Marked);
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

    [Fact]
    public async Task ProcessAsync_wrong_stage_dead_letters()
    {
        var results = new FakeResults();
        var dedup = new FakeDedup();
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: false);

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"PRD","taskId":"t1","correlationId":"c","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t1:1", 1);

        Assert.Equal(HealthyDispatchKind.DeadLetter, outcome.Kind);
        Assert.Contains("HEALTHY", outcome.DeadLetterReason ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(dedup.Marked);
    }

    [Fact]
    public async Task ProcessAsync_guardrail_block_fails_without_retry_at_delivery_1()
    {
        var order = new List<string>();
        var results = new FakeResults(onPublish: () => order.Add("publish"));
        var dedup = new FakeDedup(onMark: () => order.Add("mark"));
        var dispatcher = CreateDispatcher(results, dedup, handlerThrows: false, llmText: "# Bad heading\nnope");

        var body = """
            {"workItemId":"wi","projectId":"p","stage":"HEALTHY","taskId":"t9","correlationId":"c-g","inputVersion":1}
            """;
        var outcome = await dispatcher.ProcessAsync(body, "t9:1", deliveryCount: 1, maxDeliveryCount: 5);

        Assert.Equal(HealthyDispatchKind.DeadLetter, outcome.Kind);
        Assert.Equal(AgentResultStatus.Failed, outcome.Result!.Status);
        Assert.Contains("GuardrailBlocked", outcome.Result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(new[] { "publish", "mark" }, order);
        Assert.Contains("t9:1", dedup.Marked);
    }

    private static HealthyAgentDispatcher CreateDispatcher(
        FakeResults results,
        FakeDedup dedup,
        bool handlerThrows,
        string llmText = "ok")
    {
        var handler = new HealthyAgentHandler(
            new ThrowingOrOkLlm(handlerThrows, llmText),
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
        private readonly string _text;

        public ThrowingOrOkLlm(bool @throw, string text = "ok")
        {
            _throw = @throw;
            _text = text;
        }

        public string ProviderName => "Fixture";

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_throw)
            {
                throw new InvalidOperationException("forced");
            }

            return Task.FromResult(new LlmCompletionResult(_text, "fixture", 1, 1));
        }
    }

    private sealed class NoopBlob : IArtifactBlobStore
    {
        public Task WriteJsonAsync(string blobPath, string jsonBody, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyLedger : IEffectLedger
    {
        public Task<CommittedEffect?> TryGetCommittedAsync(
            string workItemId,
            string taskId,
            string intentKind,
            int ordinal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CommittedEffect?>(null);

        public Task<EffectCommitResult> CommitAsync(
            EffectIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectCommitResult(true, new CommittedEffect(intent.InputJson, intent.OutputJson)));
    }

    private sealed class NoopActivity : IActivityLog
    {
        public Task AppendAsync(
            string workItemId,
            string taskId,
            string stage,
            string level,
            string message,
            string? correlationId,
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

    private sealed class FakeDedup : IMessageDedup
    {
        private readonly Action? _onMark;

        public FakeDedup(Action? onMark = null) => _onMark = onMark;

        public bool Already { get; init; }
        public List<string> Marked { get; } = [];

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
