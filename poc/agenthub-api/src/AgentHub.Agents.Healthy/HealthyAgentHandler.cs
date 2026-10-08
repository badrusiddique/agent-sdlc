using System.Diagnostics;
using System.Text.Json;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Guardrails;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentHub.Agents.Healthy;

public sealed class HealthyAgentHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILlmClient _llm;
    private readonly IArtifactBlobStore _blobs;
    private readonly IEffectLedger _ledger;
    private readonly IActivityLog _activity;
    private readonly IGuardrailPipeline _guardrails;
    private readonly TimeSpan _deliberatePause;
    private readonly ILogger<HealthyAgentHandler> _logger;

    public HealthyAgentHandler(
        ILlmClient llm,
        IArtifactBlobStore blobs,
        IEffectLedger ledger,
        IActivityLog activity,
        ILogger<HealthyAgentHandler> logger,
        TimeSpan? deliberatePause = null,
        IGuardrailPipeline? guardrails = null)
    {
        _llm = llm;
        _blobs = blobs;
        _ledger = ledger;
        _activity = activity;
        _logger = logger;
        _deliberatePause = deliberatePause ?? TimeSpan.FromMilliseconds(250);
        _guardrails = guardrails ?? new LocalGuardrailPipeline();
    }

    public async Task<AgentResult> RunAsync(
        TaskMessage message,
        int deliveryCount,
        CancellationToken cancellationToken = default)
    {
        message.EnsureDefaults();
        var errors = HealthyTaskValidation.Validate(message);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var sw = Stopwatch.StartNew();
        await LogAsync(message, "info", "HEALTHY started", cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "HEALTHY run starting WorkItemId={WorkItemId} TaskId={TaskId} CorrelationId={CorrelationId} DeliveryCount={DeliveryCount}",
            message.WorkItemId,
            message.TaskId,
            message.CorrelationId,
            deliveryCount);

        if (_deliberatePause > TimeSpan.Zero)
        {
            _logger.LogInformation("HEALTHY deliberate pause {PauseMs}ms", _deliberatePause.TotalMilliseconds);
            await Task.Delay(_deliberatePause, cancellationToken).ConfigureAwait(false);
        }

        await LogAsync(message, "info", "HEALTHY progress: loading prompt", cancellationToken)
            .ConfigureAwait(false);
        var systemPrompt = PromptLoader.LoadSystemPrompt();
        var userPrompt =
            $"WorkItemId={message.WorkItemId}; ProjectId={message.ProjectId}; StateContextId={message.StateContextId}. Give today's joke or thought.";

        await EnforceGuardrailAsync(
                _guardrails.EvaluateInput(userPrompt, message.CorrelationId),
                message,
                cancellationToken)
            .ConfigureAwait(false);

        await LogAsync(message, "info", "HEALTHY progress: calling LLM", cancellationToken)
            .ConfigureAwait(false);
        var llmOutput = await RunLlmStepAsync(message, systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        var blobPath = HealthyAgentConstants.BlobPath(
            message.ProjectId,
            message.WorkItemId,
            message.InputVersion);

        await LogAsync(message, "info", "HEALTHY progress: writing artifact", cancellationToken)
            .ConfigureAwait(false);
        await WriteArtifactStepAsync(message, blobPath, llmOutput, cancellationToken)
            .ConfigureAwait(false);

        sw.Stop();
        await LogAsync(message, "info", "HEALTHY completed", cancellationToken).ConfigureAwait(false);

        return new AgentResult
        {
            WorkItemId = message.WorkItemId,
            TaskId = message.TaskId,
            Stage = HealthyAgentConstants.StageCode,
            Status = AgentResultStatus.Completed,
            CorrelationId = message.CorrelationId,
            InputVersion = message.InputVersion,
            RunId = HealthyAgentConstants.RunId(message.TaskId, message.InputVersion, deliveryCount),
            StateContextId = message.StateContextId,
            DurationMs = sw.ElapsedMilliseconds,
            Artifacts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HealthyAgentConstants.ArtifactKey] = blobPath
            },
            TokenUsage = new TokenUsage
            {
                Input = llmOutput.InputTokens,
                Output = llmOutput.OutputTokens,
                ModelId = llmOutput.ModelId
            }
        };
    }

    private async Task<LlmCompletionResult> RunLlmStepAsync(
        TaskMessage message,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var existing = await _ledger
            .TryGetCommittedAsync(
                message.WorkItemId,
                message.TaskId,
                HealthyAgentConstants.IntentLlm,
                HealthyAgentConstants.OrdinalLlm,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing?.OutputJson is { Length: > 0 })
        {
            _logger.LogInformation("HEALTHY LLM step skipped (EffectLedger hit)");
            return JsonSerializer.Deserialize<LlmCompletionResult>(existing.OutputJson, JsonOptions)
                   ?? throw new InvalidOperationException("Corrupt llm.healthy ledger OutputJson.");
        }

        var completion = await _llm
            .CompleteAsync(
                new LlmCompletionRequest(
                    systemPrompt,
                    userPrompt,
                    message.Config.ModelId,
                    message.Config.ReasoningEffort),
                cancellationToken)
            .ConfigureAwait(false);

        // Evaluate raw model text first (so OutputTooLong is meaningful), then truncate for storage.
        await EnforceGuardrailAsync(
                _guardrails.EvaluateOutput(completion.Text, systemPrompt, message.CorrelationId),
                message,
                cancellationToken)
            .ConfigureAwait(false);

        var text = Truncate(completion.Text, LocalGuardrailPipeline.DefaultMaxOutputChars);
        var stored = completion with { Text = text };
        var outputJson = JsonSerializer.Serialize(stored, JsonOptions);

        var commit = await _ledger
            .CommitAsync(
                new EffectIntent(
                    message.WorkItemId,
                    message.TaskId,
                    HealthyAgentConstants.IntentLlm,
                    HealthyAgentConstants.OrdinalLlm,
                    JsonSerializer.Serialize(new { systemPrompt, userPrompt }, JsonOptions),
                    outputJson),
                cancellationToken)
            .ConfigureAwait(false);

        if (!commit.WasFirstWriter && commit.Effect.OutputJson is { Length: > 0 })
        {
            return JsonSerializer.Deserialize<LlmCompletionResult>(commit.Effect.OutputJson, JsonOptions)
                   ?? stored;
        }

        return stored;
    }

    private async Task WriteArtifactStepAsync(
        TaskMessage message,
        string blobPath,
        LlmCompletionResult llm,
        CancellationToken cancellationToken)
    {
        var existing = await _ledger
            .TryGetCommittedAsync(
                message.WorkItemId,
                message.TaskId,
                HealthyAgentConstants.IntentArtifact,
                HealthyAgentConstants.OrdinalArtifact,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogInformation("HEALTHY artifact step skipped (EffectLedger hit)");
            return;
        }

        var body = JsonSerializer.Serialize(
            new
            {
                message = "healthy",
                agent = HealthyAgentConstants.AgentName,
                workItemId = message.WorkItemId,
                projectId = message.ProjectId,
                inputVersion = message.InputVersion,
                stateContextId = message.StateContextId,
                llmProvider = _llm.ProviderName,
                thought = llm.Text,
                writtenAtUtc = DateTimeOffset.UtcNow
            },
            JsonOptions);

        // Secrets scan on artifact body before blob write (brain-dump item 6).
        await EnforceGuardrailAsync(
                _guardrails.EvaluateOutput(body, systemPrompt: string.Empty, message.CorrelationId),
                message,
                cancellationToken)
            .ConfigureAwait(false);

        await _blobs.WriteJsonAsync(blobPath, body, cancellationToken).ConfigureAwait(false);

        await _ledger
            .CommitAsync(
                new EffectIntent(
                    message.WorkItemId,
                    message.TaskId,
                    HealthyAgentConstants.IntentArtifact,
                    HealthyAgentConstants.OrdinalArtifact,
                    null,
                    JsonSerializer.Serialize(
                        new { blobPath, bytes = body.Length, contentType = "application/json" },
                        JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnforceGuardrailAsync(
        GuardrailVerdict verdict,
        TaskMessage message,
        CancellationToken cancellationToken)
    {
        var codes = string.Join(',', verdict.ReasonCodes);
        _logger.LogInformation(
            "HEALTHY guardrail stage={Stage} decision={Decision} codes={Codes} CorrelationId={CorrelationId}",
            verdict.Stage,
            verdict.Decision,
            codes,
            verdict.CorrelationId);

        await LogAsync(
                message,
                verdict.Decision == GuardrailDecision.Allow ? "info" : "warn",
                $"HEALTHY guardrail {verdict.Stage}={verdict.Decision} codes={codes}",
                cancellationToken)
            .ConfigureAwait(false);

        if (verdict.Decision == GuardrailDecision.Block)
        {
            throw new GuardrailBlockedException(verdict);
        }
    }

    private Task LogAsync(
        TaskMessage message,
        string level,
        string text,
        CancellationToken cancellationToken) =>
        _activity.AppendAsync(
            message.WorkItemId,
            message.TaskId,
            HealthyAgentConstants.StageCode,
            level,
            text,
            message.CorrelationId,
            cancellationToken);

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..(max - 14)] + "...[truncated]";
    }
}
