using AgentHub.Agents.Pipeline;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class PipelineAgentHandlerTests
{
    [Fact]
    public async Task RunAsync_prd_writes_prd_artifacts()
    {
        var blobs = new FakeBlobStore();
        var handler = CreateHandler(blobs);
        var message = BaseMessage(PipelineAgentConstants.StagePrd);

        var result = await handler.RunAsync(message, 1);

        Assert.Equal(AgentResultStatus.Completed, result.Status);
        Assert.Contains("prd", result.Artifacts.Keys);
        Assert.Contains(blobs.Writes, w => w.Path.EndsWith("prd.json", StringComparison.Ordinal));
        Assert.Contains(result.Steps, s => s.Id == "prd.draft" && s.Status == "Completed");
        Assert.Contains(result.Steps, s => s.Id == "prd.persistJson" && s.Status == "Completed");
        Assert.Contains(result.Steps, s => s.Id == "prd.persistMd" && s.Status == "Completed");
    }

    [Fact]
    public async Task RunAsync_implementation_generates_calculator_from_request()
    {
        var blobs = new FakeBlobStore();
        var handler = CreateHandler(blobs);
        var mono = Path.Combine(
            Path.GetTempPath(),
            "bgx-poc-tests",
            $"project-p1-work-item-wi1-{Guid.NewGuid():N}");
        var message = BaseMessage(PipelineAgentConstants.StageIaCodegen);
        message.Context.StackProfile = "fe-be";
        message.Context.WorkspaceRoot = mono;

        var result = await handler.RunAsync(message, 1);

        Assert.Equal(AgentResultStatus.Completed, result.Status);
        Assert.True(Directory.Exists(Path.Combine(mono, "backend")));
        Assert.True(Directory.Exists(Path.Combine(mono, "frontend")));
        Assert.True(File.Exists(Path.Combine(mono, "backend", "CalculatorApi.csproj")));
        Assert.True(result.Outputs.ContainsKey("headCommit"));
        Assert.True(result.Outputs.ContainsKey("monoFolder"));
        Assert.Equal("Calculator", result.Outputs.GetValueOrDefault("appKind"));
        // code-state is produced by graphify after IA_REVIEW, not during codegen
        Assert.DoesNotContain("codeState", result.Artifacts.Keys);
        Assert.Contains(result.Steps, s => s.Id == "ia.codegen.generate" && s.Status == "Completed");
    }

    private static PipelineAgentHandler CreateHandler(FakeBlobStore blobs) =>
        new(
            new FakeLlm(),
            blobs,
            new InMemoryEffectLedger(),
            new InMemoryActivityLog(),
            new FakeEvents(),
            NullLogger<PipelineAgentHandler>.Instance);

    private static TaskMessage BaseMessage(string stage) =>
        new()
        {
            WorkItemId = "wi1",
            ProjectId = "p1",
            TaskId = "t1",
            CorrelationId = "c1",
            InputVersion = 1,
            OutputRevision = 1,
            Stage = stage,
            BlobPrefix = "p1/wi1/v1/",
            Context = new TaskMessageContext
            {
                UserRequest = "Create a calculator app that adds two numbers",
                StackProfile = "fe-be",
                ProjectMode = "New",
                ProjectName = "Calculator"
            }
        };

    private sealed class FakeLlm : ILlmClient
    {
        public string ProviderName => "Fixture";

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmCompletionResult("""{"headline":"Calculator"}""", "fixture", 1, 1));
    }

    private sealed class FakeBlobStore : IArtifactBlobStore
    {
        public List<(string Path, string Body)> Writes { get; } = [];

        public Task WriteJsonAsync(string blobPath, string jsonBody, CancellationToken cancellationToken = default)
        {
            Writes.Add((blobPath, jsonBody));
            return Task.CompletedTask;
        }

        public Task<string?> TryReadJsonAsync(string blobPath, CancellationToken cancellationToken = default)
        {
            var hit = Writes.LastOrDefault(w => w.Path == blobPath);
            return Task.FromResult<string?>(string.IsNullOrEmpty(hit.Path) ? null : hit.Body);
        }
    }

    private sealed class InMemoryEffectLedger : IEffectLedger
    {
        private readonly Dictionary<string, CommittedEffect> _rows = new(StringComparer.Ordinal);

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
            return Task.FromResult(new EffectCommitResult(true, effect));
        }
    }

    private sealed class InMemoryActivityLog : IActivityLog
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

    private sealed class FakeEvents : IAgentEventPublisher
    {
        public Task PublishEventAsync(AgentPipelineEvent pipelineEvent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
