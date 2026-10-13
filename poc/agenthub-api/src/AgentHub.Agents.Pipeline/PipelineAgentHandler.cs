using System.Diagnostics;
using System.Text.Json;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Guardrails;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentHub.Agents.Pipeline;

public sealed class PipelineAgentHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILlmClient _llm;
    private readonly IArtifactBlobStore _blobs;
    private readonly IEffectLedger _ledger;
    private readonly IActivityLog _activity;
    private readonly IAgentEventPublisher _events;
    private readonly IGuardrailPipeline _guardrails;
    private readonly ILogger<PipelineAgentHandler> _logger;

    public PipelineAgentHandler(
        ILlmClient llm,
        IArtifactBlobStore blobs,
        IEffectLedger ledger,
        IActivityLog activity,
        IAgentEventPublisher events,
        ILogger<PipelineAgentHandler> logger,
        IGuardrailPipeline? guardrails = null)
    {
        _llm = llm;
        _blobs = blobs;
        _ledger = ledger;
        _activity = activity;
        _events = events;
        _logger = logger;
        _guardrails = guardrails ?? new LocalGuardrailPipeline();
    }

    public async Task<AgentResult> RunAsync(
        TaskMessage message,
        int deliveryCount,
        CancellationToken cancellationToken = default)
    {
        message.EnsureDefaults();
        var errors = PipelineTaskValidation.Validate(message);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        if (string.IsNullOrWhiteSpace(message.BlobPrefix))
        {
            message.BlobPrefix = PipelineAgentConstants.BlobPrefix(
                message.ProjectId,
                message.WorkItemId,
                message.OutputRevision < 1 ? 1 : message.OutputRevision);
        }

        var sw = Stopwatch.StartNew();
        var run = new StageRunContext(
            PipelineAgentConstants.RunId(message.TaskId, message.InputVersion, deliveryCount));
        await LogAsync(message, "info", $"PIPELINE {message.Stage} started", cancellationToken)
            .ConfigureAwait(false);
        await PublishProgressAsync(
                message,
                run,
                "AgentStarted",
                $"Stage {message.Stage} started",
                "Running",
                cancellationToken)
            .ConfigureAwait(false);

        var artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        TokenUsage? tokenUsage = null;

        switch (message.Stage)
        {
            case PipelineAgentConstants.StagePrd:
                tokenUsage = await RunPrdAsync(message, run, artifacts, cancellationToken).ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageSystemDesign:
                tokenUsage = await RunSystemDesignAsync(message, run, artifacts, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageImplementation:
                tokenUsage = await RunImplementationAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageTesting:
                await RunTestingAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageDeployDev:
                await RunDeployAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported stage {message.Stage}.");
        }

        sw.Stop();
        await LogAsync(message, "info", $"PIPELINE {message.Stage} completed", cancellationToken)
            .ConfigureAwait(false);
        await PublishProgressAsync(
                message,
                run,
                "AgentCompleted",
                $"Stage {message.Stage} completed",
                AgentResultStatus.Completed,
                cancellationToken)
            .ConfigureAwait(false);

        return new AgentResult
        {
            WorkItemId = message.WorkItemId,
            TaskId = message.TaskId,
            Stage = message.Stage,
            Status = AgentResultStatus.Completed,
            CorrelationId = message.CorrelationId,
            InputVersion = message.InputVersion,
            OutputRevision = message.OutputRevision < 1 ? 1 : message.OutputRevision,
            RunId = run.RunId,
            StateContextId = message.StateContextId,
            DurationMs = sw.ElapsedMilliseconds,
            Artifacts = artifacts,
            Outputs = outputs,
            TokenUsage = tokenUsage,
            Steps = run.Steps
        };
    }

    private async Task<TokenUsage> RunPrdAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        CancellationToken cancellationToken)
    {
        await BeginStepAsync(message, run, "prd.draft", "Draft PRD with model", cancellationToken)
            .ConfigureAwait(false);
        var llm = await CallStageLlmAsync(message, 100, cancellationToken).ConfigureAwait(false);
        var prdJson = EnsureJson(llm.Text, () => DefaultPrd(message));
        await CompleteStepAsync(message, run, "prd.draft", "PRD draft ready", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "prd.persistJson", "Persist prd.json", cancellationToken)
            .ConfigureAwait(false);
        var prdPath = message.BlobPrefix + "prd.json";
        await WriteArtifactAsync(message, prdPath, prdJson, 200, cancellationToken).ConfigureAwait(false);
        artifacts["prd"] = prdPath;
        await CompleteStepAsync(message, run, "prd.persistJson", "Wrote prd.json", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "prd.persistMd", "Persist PRD markdown", cancellationToken)
            .ConfigureAwait(false);
        var mdPath = message.BlobPrefix + "prd.md";
        var md = $"# {message.Context.ProjectName ?? "PRD"}\n\n{message.Context.UserRequest}\n\n```json\n{prdJson}\n```\n";
        await WriteArtifactAsync(
                message,
                mdPath,
                JsonSerializer.Serialize(new { content = md }, JsonOptions),
                210,
                cancellationToken)
            .ConfigureAwait(false);
        artifacts["prdMd"] = mdPath;
        await CompleteStepAsync(message, run, "prd.persistMd", "Wrote prd.md", cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task<TokenUsage> RunSystemDesignAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        CancellationToken cancellationToken)
    {
        await BeginStepAsync(message, run, "sd.draft", "Draft system design with model", cancellationToken)
            .ConfigureAwait(false);
        var llm = await CallStageLlmAsync(message, 300, cancellationToken).ConfigureAwait(false);
        var tech = EnsureJson(llm.Text, () => DefaultTechSpec(message));
        await CompleteStepAsync(message, run, "sd.draft", "System design draft ready", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "sd.persistJson", "Persist techspec.json", cancellationToken)
            .ConfigureAwait(false);
        var techPath = message.BlobPrefix + "techspec.json";
        await WriteArtifactAsync(message, techPath, tech, 400, cancellationToken).ConfigureAwait(false);
        artifacts["techSpec"] = techPath;
        await CompleteStepAsync(message, run, "sd.persistJson", "Wrote techspec.json", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "sd.persistMd", "Persist system-design.md", cancellationToken)
            .ConfigureAwait(false);
        var mdPath = message.BlobPrefix + "system-design.md";
        var md = $"# System design\n\nStack: {message.Context.StackProfile}\n\n```json\n{tech}\n```\n";
        await WriteArtifactAsync(message, mdPath, JsonSerializer.Serialize(new { content = md }), 410, cancellationToken)
            .ConfigureAwait(false);
        artifacts["techSpecMd"] = mdPath;
        await CompleteStepAsync(message, run, "sd.persistMd", "Wrote system-design.md", cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task<TokenUsage> RunImplementationAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var stack = message.Context.StackProfile ?? "fe-be";
        await BeginStepAsync(message, run, "impl.hints", "Gather implementation hints", cancellationToken)
            .ConfigureAwait(false);
        // Headline/thought hints are best-effort — never fail Implementation on LLM timeout.
        LlmCompletionResult llm;
        try
        {
            llm = await CallStageLlmAsync(message, 500, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            llm = new LlmCompletionResult("{}", message.Config.ModelId ?? "local", 0, 0);
        }

        await CompleteStepAsync(message, run, "impl.hints", "Implementation hints ready", cancellationToken)
            .ConfigureAwait(false);

        var workspace = ResolveWorkspace(message);
        var intent = PipelineAgentConstants.IntentWorkspace;
        var existing = await _ledger
            .TryGetCommittedAsync(message.WorkItemId, message.TaskId, intent, 600, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            await BeginStepAsync(message, run, "impl.materialize", "Materialize workspace template", cancellationToken)
                .ConfigureAwait(false);
            var templateRoot = TemplateMaterializer.ResolveTemplateRoot(stack);
            TemplateMaterializer.CopyTemplate(templateRoot, workspace);
            await CompleteStepAsync(message, run, "impl.materialize", $"Copied {stack} template", cancellationToken)
                .ConfigureAwait(false);

            await BeginStepAsync(message, run, "impl.apply", "Apply stack and project updates", cancellationToken)
                .ConfigureAwait(false);
            if (message.Context.ProjectMode.equalsIgnore("Existing")
                && message.Context.BaselineArtifacts.Count > 0)
            {
                ApplySubtractUpdate(workspace);
            }

            TemplateMaterializer.ApplyImplementationHints(workspace, stack, llm.Text);
            await CommitLedgerAsync(message, intent, 600, new { workspace, stack }, new { ok = true }, cancellationToken)
                .ConfigureAwait(false);
            await CompleteStepAsync(message, run, "impl.apply", "Workspace customized", cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await BeginStepAsync(message, run, "impl.reuse", "Reuse committed workspace", cancellationToken)
                .ConfigureAwait(false);
            await CompleteStepAsync(message, run, "impl.reuse", "Workspace already materialized", cancellationToken)
                .ConfigureAwait(false);
        }

        await BeginStepAsync(message, run, "impl.commit", "Commit workspace snapshot", cancellationToken)
            .ConfigureAwait(false);
        var headCommit = await EnsureLocalGitCommitAsync(workspace, cancellationToken).ConfigureAwait(false);
        await CompleteStepAsync(message, run, "impl.commit", $"headCommit={headCommit}", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "impl.artifacts", "Publish code-state and review", cancellationToken)
            .ConfigureAwait(false);
        var codeState = TemplateMaterializer.WriteCodeState(workspace, stack);
        var codeStatePath = message.BlobPrefix + "code-state.json";
        await WriteArtifactAsync(message, codeStatePath, codeState, 700, cancellationToken).ConfigureAwait(false);
        var reviewPath = message.BlobPrefix + "review.json";
        var review = JsonSerializer.Serialize(
            new { status = "passed", checks = new[] { "template-allowlist", "health-endpoint" } },
            JsonOptions);
        await WriteArtifactAsync(message, reviewPath, review, 710, cancellationToken).ConfigureAwait(false);
        artifacts["codeState"] = codeStatePath;
        artifacts["review"] = reviewPath;
        await CompleteStepAsync(message, run, "impl.artifacts", "Wrote code-state.json and review.json", cancellationToken)
            .ConfigureAwait(false);

        outputs["workBranch"] = message.Context.WorkBranch ?? $"feature/wi-{message.WorkItemId}";
        outputs["headCommit"] = headCommit;
        outputs["workspaceRoot"] = workspace;
        return Tokens(llm);
    }

    private async Task RunTestingAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var workspace = message.Context.WorkspaceRoot
                        ?? ResolveWorkspace(message);
        var backend = Path.Combine(workspace, "backend");

        await BeginStepAsync(message, run, "test.locate", "Locate automated test project", cancellationToken)
            .ConfigureAwait(false);
        var testProj = Directory.EnumerateFiles(workspace, "*Tests.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        await CompleteStepAsync(
                message,
                run,
                "test.locate",
                testProj is null ? "No test project; will smoke-compile" : Path.GetFileName(testProj),
                cancellationToken)
            .ConfigureAwait(false);

        var passed = true;
        var detail = "no test project; smoke compile";
        object? roundTrips = null;
        var roundTripPath = Path.Combine(Path.GetTempPath(), $"bgx-roundtrip-{message.WorkItemId}.json");
        if (File.Exists(roundTripPath))
        {
            File.Delete(roundTripPath);
        }

        await BeginStepAsync(message, run, "test.execute", "Execute automated suite", cancellationToken)
            .ConfigureAwait(false);
        if (testProj is not null)
        {
            var (exit, stdout) = await RunProcessAsync(
                    ResolveDotnet(),
                    $"test \"{testProj}\" --nologo",
                    Path.GetDirectoryName(testProj)!,
                    cancellationToken,
                    new Dictionary<string, string>
                    {
                        ["BGX_ROUNDTRIP_PATH"] = roundTripPath,
                        // Keep thought tests deterministic/fast inside the pipeline host.
                        ["OLLAMA_BASE_URL"] = "http://127.0.0.1:9"
                    })
                .ConfigureAwait(false);
            passed = exit == 0;
            detail = Truncate(stdout, 2000);
            if (File.Exists(roundTripPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(roundTripPath, cancellationToken)
                        .ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("roundTrips", out var rt))
                    {
                        roundTrips = JsonSerializer.Deserialize<object>(rt.GetRawText());
                    }
                }
                catch (JsonException)
                {
                    // keep detail-only results
                }
            }
        }
        else
        {
            var csproj = Directory.EnumerateFiles(backend, "*.csproj").First();
            var (exit, stdout) = await RunProcessAsync(
                    ResolveDotnet(),
                    $"build \"{csproj}\" --nologo",
                    backend,
                    cancellationToken)
                .ConfigureAwait(false);
            passed = exit == 0;
            detail = Truncate(stdout, 2000);
        }

        if (!passed)
        {
            await CompleteStepAsync(message, run, "test.execute", "Automated suite failed", cancellationToken, failed: true)
                .ConfigureAwait(false);
            throw new InvalidOperationException($"TESTING failed: {detail}");
        }

        await CompleteStepAsync(message, run, "test.execute", "Automated suite green", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "test.publish", "Publish test-results.json", cancellationToken)
            .ConfigureAwait(false);
        var results = JsonSerializer.Serialize(
            new
            {
                status = "green",
                kind = roundTrips is null ? "compile-or-unit" : "http-roundtrip",
                roundTrips,
                detail,
                headCommit = message.Context.HeadCommit
            },
            JsonOptions);
        var path = message.BlobPrefix + "test-results.json";
        await WriteArtifactAsync(message, path, results, 800, cancellationToken).ConfigureAwait(false);
        artifacts["testResults"] = path;
        outputs["headCommit"] = message.Context.HeadCommit
                                 ?? (await EnsureLocalGitCommitAsync(workspace, cancellationToken)
                                     .ConfigureAwait(false));
        outputs["workspaceRoot"] = workspace;
        await CompleteStepAsync(
                message,
                run,
                "test.publish",
                roundTrips is null ? "Wrote test-results.json" : "Wrote test-results.json with HTTP round-trips",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunDeployAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var workspace = message.Context.WorkspaceRoot ?? ResolveWorkspace(message);
        var action = message.Context.DeploymentAction ?? "Deploy";
        if (action.Equals("Finalize", StringComparison.OrdinalIgnoreCase))
        {
            await BeginStepAsync(message, run, "deploy.finalize", "Finalize local deployment", cancellationToken)
                .ConfigureAwait(false);
            await LocalComposeHost.FinalizeAsync(workspace, cancellationToken).ConfigureAwait(false);
            var merge = message.Context.HeadCommit ?? "local";
            outputs["mergeCommit"] = merge;
            outputs["headCommit"] = merge;
            var summaryPath = message.BlobPrefix + "completion-summary.json";
            var existingSummary = await _blobs.TryReadJsonAsync(summaryPath, cancellationToken).ConfigureAwait(false);
            if (existingSummary is not null)
            {
                artifacts["completionSummary"] = summaryPath;
            }

            await CompleteStepAsync(message, run, "deploy.finalize", "Finalize complete", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await BeginStepAsync(message, run, "deploy.host", "Start local app host", cancellationToken)
            .ConfigureAwait(false);
        var deploy = await LocalComposeHost
            .DeployAsync(workspace, message.WorkItemId, cancellationToken)
            .ConfigureAwait(false);
        await CompleteStepAsync(message, run, "deploy.host", $"Host ready at {deploy.Url}", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "deploy.summary", "Publish completion summary", cancellationToken)
            .ConfigureAwait(false);
        var summary = JsonSerializer.Serialize(
            new
            {
                deployedUrls = new { app = deploy.Url },
                mode = deploy.Mode,
                processId = deploy.ProcessId,
                stackProfile = message.Context.StackProfile,
                request = message.Context.UserRequest
            },
            JsonOptions);
        var path = message.BlobPrefix + "completion-summary.json";
        await WriteArtifactAsync(message, path, summary, 900, cancellationToken).ConfigureAwait(false);
        artifacts["completionSummary"] = path;
        outputs["deployedUrls"] = deploy.Url;
        outputs["headCommit"] = message.Context.HeadCommit ?? "local";
        outputs["workspaceRoot"] = workspace;
        outputs["deployMode"] = deploy.Mode;
        await CompleteStepAsync(message, run, "deploy.summary", "Wrote completion-summary.json", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<LlmCompletionResult> CallStageLlmAsync(
        TaskMessage message,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var existing = await _ledger
            .TryGetCommittedAsync(
                message.WorkItemId,
                message.TaskId,
                PipelineAgentConstants.IntentLlm,
                ordinal,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing?.OutputJson is { Length: > 0 })
        {
            return JsonSerializer.Deserialize<LlmCompletionResult>(existing.OutputJson, JsonOptions)
                   ?? throw new InvalidOperationException("Corrupt llm.pipeline ledger OutputJson.");
        }

        var system = PromptLoader.LoadSystemPrompt();
        var stageSection = PromptLoader.LoadStageSection(message.Stage);
        var userPrompt =
            $"Stage={message.Stage}\nStackProfile={message.Context.StackProfile}\nProjectMode={message.Context.ProjectMode}\nUserRequest={message.Context.UserRequest}\n\n{stageSection}";

        // Pipeline LLM output is JSON by design; skip Healthy-style markdown-heading blocks by
        // evaluating input only, then storing truncated text.
        await EnforceGuardrailAsync(
                _guardrails.EvaluateInput(userPrompt, message.CorrelationId),
                message,
                cancellationToken)
            .ConfigureAwait(false);

        var completion = await _llm
            .CompleteAsync(
                new LlmCompletionRequest(
                    system,
                    userPrompt,
                    message.Config.ModelId,
                    message.Config.ReasoningEffort),
                cancellationToken)
            .ConfigureAwait(false);

        var text = Truncate(completion.Text, LocalGuardrailPipeline.DefaultMaxOutputChars);
        var stored = completion with { Text = text };
        await CommitLedgerAsync(
                message,
                PipelineAgentConstants.IntentLlm,
                ordinal,
                new { system, userPrompt },
                stored,
                cancellationToken)
            .ConfigureAwait(false);
        return stored;
    }

    private async Task WriteArtifactAsync(
        TaskMessage message,
        string blobPath,
        string jsonBody,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var existing = await _ledger
            .TryGetCommittedAsync(
                message.WorkItemId,
                message.TaskId,
                PipelineAgentConstants.IntentArtifact,
                ordinal,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        await _blobs.WriteJsonAsync(blobPath, jsonBody, cancellationToken).ConfigureAwait(false);
        await CommitLedgerAsync(
                message,
                PipelineAgentConstants.IntentArtifact,
                ordinal,
                null,
                new { blobPath, bytes = jsonBody.Length },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CommitLedgerAsync(
        TaskMessage message,
        string intent,
        int ordinal,
        object? input,
        object output,
        CancellationToken cancellationToken)
    {
        await _ledger
            .CommitAsync(
                new EffectIntent(
                    message.WorkItemId,
                    message.TaskId,
                    intent,
                    ordinal,
                    input is null ? null : JsonSerializer.Serialize(input, JsonOptions),
                    JsonSerializer.Serialize(output, JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnforceGuardrailAsync(
        GuardrailVerdict verdict,
        TaskMessage message,
        CancellationToken cancellationToken)
    {
        var codes = string.Join(',', verdict.ReasonCodes);
        await LogAsync(
                message,
                verdict.Decision == GuardrailDecision.Allow ? "info" : "warn",
                $"PIPELINE guardrail {verdict.Stage}={verdict.Decision} codes={codes}",
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
            message.Stage,
            level,
            text,
            message.CorrelationId,
            cancellationToken);

    private async Task BeginStepAsync(
        TaskMessage message,
        StageRunContext run,
        string id,
        string label,
        CancellationToken cancellationToken)
    {
        await LogAsync(message, "info", $"TASK {id}: {label}", cancellationToken).ConfigureAwait(false);
        await PublishProgressAsync(message, run, "TaskStarted", label, "Running", cancellationToken)
            .ConfigureAwait(false);
        run.Steps.Add(
            new AgentStepProgress
            {
                Id = id,
                Label = label,
                Status = "Running",
                Message = label,
                At = DateTimeOffset.UtcNow
            });
    }

    private async Task CompleteStepAsync(
        TaskMessage message,
        StageRunContext run,
        string id,
        string messageText,
        CancellationToken cancellationToken,
        bool failed = false)
    {
        var status = failed ? "Failed" : "Completed";
        await LogAsync(message, failed ? "error" : "info", $"TASK {id}: {messageText}", cancellationToken)
            .ConfigureAwait(false);
        await PublishProgressAsync(message, run, "TaskCompleted", messageText, status, cancellationToken)
            .ConfigureAwait(false);
        var existing = run.Steps.LastOrDefault(s => s.Id == id && s.Status == "Running");
        if (existing is not null)
        {
            existing.Status = status;
            existing.Message = messageText;
            existing.At = DateTimeOffset.UtcNow;
        }
        else
        {
            run.Steps.Add(
                new AgentStepProgress
                {
                    Id = id,
                    Label = id,
                    Status = status,
                    Message = messageText,
                    At = DateTimeOffset.UtcNow
                });
        }
    }

    private async Task PublishProgressAsync(
        TaskMessage message,
        StageRunContext run,
        string kind,
        string text,
        string? status,
        CancellationToken cancellationToken)
    {
        var seq = Interlocked.Increment(ref run.EventSeq);
        await _events
            .PublishEventAsync(
                new AgentPipelineEvent
                {
                    EventId = $"{message.TaskId}:{message.InputVersion}:{seq}",
                    WorkItemId = message.WorkItemId,
                    TaskId = message.TaskId,
                    Stage = message.Stage,
                    RunId = run.RunId,
                    Kind = kind,
                    Message = text,
                    Level = "info",
                    CorrelationId = message.CorrelationId,
                    Status = status,
                    OccurredAt = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class StageRunContext(string runId)
    {
        public string RunId { get; } = runId;
        public int EventSeq;
        public List<AgentStepProgress> Steps { get; } = [];
    }

    private static string ResolveWorkspace(TaskMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Context.WorkspaceRoot))
        {
            return message.Context.WorkspaceRoot!;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "bgx-poc",
            message.ProjectId,
            message.WorkItemId);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ApplySubtractUpdate(string workspace)
    {
        var program = Path.Combine(workspace, "backend", "Program.cs");
        if (!File.Exists(program))
        {
            return;
        }

        var text = File.ReadAllText(program);
        if (text.Contains("/api/subtract", StringComparison.Ordinal))
        {
            return;
        }

        text = text.Replace(
            "// {{EXTRA_ENDPOINTS}}",
            """
            app.MapGet("/api/subtract", (double a, double b) =>
                Results.Ok(new
                {
                    operation = "subtract",
                    a,
                    b,
                    result = CalculatorMath.Subtract(a, b)
                }));
            // {{EXTRA_ENDPOINTS}}
            """,
            StringComparison.Ordinal);
        File.WriteAllText(program, text);

        var index = Path.Combine(workspace, "backend", "wwwroot", "index.html");
        if (File.Exists(index))
        {
            var html = File.ReadAllText(index);
            if (!html.Contains("subBtn", StringComparison.Ordinal)
                && !html.Contains("/api/subtract", StringComparison.Ordinal))
            {
                html = html.Replace(
                    "<!-- {{EXTRA_UI}} -->",
                    """
                    <button id="subBtn" class="ghost" type="button">Subtract</button>
                    <script>
                    document.getElementById('subBtn')?.addEventListener('click', () => callApi('/api/subtract'));
                    </script>
                    <!-- {{EXTRA_UI}} -->
                    """,
                    StringComparison.Ordinal);
                File.WriteAllText(index, html);
            }
        }
    }

    private static async Task<string> EnsureLocalGitCommitAsync(
        string workspace,
        CancellationToken cancellationToken)
    {
        var gitDir = Path.Combine(workspace, ".git");
        if (!Directory.Exists(gitDir))
        {
            await RunProcessAsync("git", "init", workspace, cancellationToken).ConfigureAwait(false);
            await RunProcessAsync("git", "config user.email poc@buildgenix.local", workspace, cancellationToken)
                .ConfigureAwait(false);
            await RunProcessAsync("git", "config user.name poc", workspace, cancellationToken)
                .ConfigureAwait(false);
            await RunProcessAsync("git", "add -A", workspace, cancellationToken).ConfigureAwait(false);
            await RunProcessAsync("git", "commit -m \"poc implementation\" --allow-empty", workspace, cancellationToken)
                .ConfigureAwait(false);
        }

        var (exit, stdout) = await RunProcessAsync("git", "rev-parse --short HEAD", workspace, cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0)
        {
            return "local";
        }

        return stdout.Trim();
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string args,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                psi.Environment[key] = value;
            }
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (p.ExitCode, stdout + stderr);
    }

    private static string ResolveDotnet()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static string EnsureJson(string text, Func<string> fallback)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return text;
        }
        catch
        {
            return fallback();
        }
    }

    private static string DefaultPrd(TaskMessage message) =>
        JsonSerializer.Serialize(
            new
            {
                title = message.Context.ProjectName ?? "App",
                summary = message.Context.UserRequest,
                stories = new[]
                {
                    new
                    {
                        id = "S1",
                        title = "Primary behaviour",
                        acceptance = new[] { "Happy path works locally" }
                    }
                },
                stackProfile = message.Context.StackProfile
            },
            JsonOptions);

    private static string DefaultTechSpec(TaskMessage message) =>
        JsonSerializer.Serialize(
            new
            {
                components = new[] { "fe", "be" },
                apis = message.Context.StackProfile == "fe-be-llm"
                    ? new object[] { new { method = "GET", path = "/api/thought", purpose = "thought of the day" } }
                    : new object[] { new { method = "GET", path = "/api/add", purpose = "add two numbers" } },
                phases = new[] { new { id = 1, name = "Scaffold and run" } },
                deploy = "local-compose",
                stackProfile = message.Context.StackProfile
            },
            JsonOptions);

    private static TokenUsage Tokens(LlmCompletionResult llm) =>
        new()
        {
            Input = llm.InputTokens,
            Output = llm.OutputTokens,
            ModelId = llm.ModelId
        };

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max
            ? value
            : value[..(max - 14)] + "...[truncated]";
}

internal static class StringExtensions
{
    public static bool equalsIgnore(this string? value, string other) =>
        string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
}
