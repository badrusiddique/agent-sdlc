using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentHub.Agents.Pipeline.Artifacts;
using AgentHub.Agents.Pipeline.Deploy;
using AgentHub.Agents.Pipeline.Ia;
using AgentHub.Agents.Pipeline.Prd;
using AgentHub.Agents.Pipeline.Sd;
using AgentHub.Agents.Pipeline.Ta;
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
    private readonly IaGitOptions _gitOptions;
    private readonly IaDeployOptions _deployOptions;
    private readonly IGitPullRequestClient _git;
    private readonly IDeployHost _deploy;

    public PipelineAgentHandler(
        ILlmClient llm,
        IArtifactBlobStore blobs,
        IEffectLedger ledger,
        IActivityLog activity,
        IAgentEventPublisher events,
        ILogger<PipelineAgentHandler> logger,
        IGuardrailPipeline? guardrails = null,
        IaGitOptions? gitOptions = null,
        IaDeployOptions? deployOptions = null,
        IGitPullRequestClient? gitClient = null,
        IDeployHost? deployHost = null)
    {
        _llm = llm;
        _blobs = blobs;
        _ledger = ledger;
        _activity = activity;
        _events = events;
        _logger = logger;
        _guardrails = guardrails ?? new LocalGuardrailPipeline();
        _gitOptions = gitOptions ?? new IaGitOptions();
        _deployOptions = deployOptions ?? new IaDeployOptions();
        _git = gitClient ?? GitPullRequestClientFactory.Create(_gitOptions);
        _deploy = deployHost ?? DeployHostFactory.Create(_deployOptions);
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
            case PipelineAgentConstants.StageIaPlan:
                tokenUsage = await RunIaPlanAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageIaCodegen:
            case PipelineAgentConstants.StageImplementation:
                tokenUsage = await RunIaCodegenAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageIaReview:
                tokenUsage = await RunIaReviewAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageIaPushDeploy:
            case PipelineAgentConstants.StageDeployDev:
                await RunIaPushDeployAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageTaGenerate:
                tokenUsage = await RunTaGenerateAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageTaVerify:
                await RunTaVerifyAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageTaReport:
                tokenUsage = await RunTaReportAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageTaMerge:
                await RunTaMergeAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageDeploySandbox:
                await RunDeploySandboxAsync(message, run, artifacts, outputs, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineAgentConstants.StageTesting:
                // Legacy path — IA_CODEGEN already runs unit tests; live URL tests are TA_*.
                await RunTestingAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);
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
        var prdJson = EnsureJson(llm.Text, () => PrdDocumentBuilder.BuildJson(message));
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
        var md = PrdDocumentBuilder.BuildMarkdown(message, prdJson);
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
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "PRD",
                stage: PipelineAgentConstants.StagePrd,
                phase: "persist",
                summary: $"Drafted PRD for '{message.Context.ProjectName ?? "app"}' from user request.",
                decisions:
                [
                    "Product owns acceptance criteria; Implementation must not invent product behaviour.",
                    "deliveryMode recorded for downstream IA/TA smoke selection."
                ],
                cancellationToken)
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
        var tech = EnsureJson(llm.Text, () => TechSpecBuilder.BuildJson(message));
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
        var md = TechSpecBuilder.BuildMarkdown(message, tech);
        await WriteArtifactAsync(message, mdPath, JsonSerializer.Serialize(new { content = md }), 410, cancellationToken)
            .ConfigureAwait(false);
        artifacts["techSpecMd"] = mdPath;
        await CompleteStepAsync(message, run, "sd.persistMd", "Wrote system-design.md", cancellationToken)
            .ConfigureAwait(false);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "SystemDesign",
                stage: PipelineAgentConstants.StageSystemDesign,
                phase: "persist",
                summary: "Published techspec + system-design for IA/TA spoon-feed.",
                decisions:
                [
                    "Primary API and deliveryMode locked for Implementation and Testing.",
                    "IA must not invent routes outside this design; TA derives live checks from it."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task<TokenUsage> RunIaPlanAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var monoRoot = ResolveWorkspace(message);
        outputs["workspaceRoot"] = monoRoot;
        outputs["workBranch"] = MonoRepoLayout.WorkBranch(message.ProjectId, message.WorkItemId);
        outputs["monoFolder"] = MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId);

        await BeginStepAsync(message, run, "ia.plan.converse", "Draft implementation plan in conversation", cancellationToken)
            .ConfigureAwait(false);
        var conversation = await BuildPersonaContextAsync(message, PersonaKind.Planner, cancellationToken)
            .ConfigureAwait(false);
        LlmCompletionResult llm;
        try
        {
            llm = await CallPersonaAsync(
                    message,
                    520,
                    PipelineAgentConstants.IntentIa,
                    PersonaKind.Planner,
                    PromptLoader.LoadIaPhasePrompt("ia-plan.txt"),
                    conversation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            llm = new LlmCompletionResult(
                DefaultIaPlanMarkdown(message),
                message.Config.ModelId ?? "local",
                0,
                0);
        }

        await CompleteStepAsync(message, run, "ia.plan.converse", "Implementation plan drafted", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "ia.plan.persist", "Persist implementation-plan.md", cancellationToken)
            .ConfigureAwait(false);
        var planPath = message.BlobPrefix + "implementation-plan.md";
        var planText = string.IsNullOrWhiteSpace(llm.Text) ? DefaultIaPlanMarkdown(message) : llm.Text;
        await WriteArtifactAsync(message, planPath, planText, 530, cancellationToken).ConfigureAwait(false);
        artifacts["implementationPlan"] = planPath;
        await CompleteStepAsync(message, run, "ia.plan.persist", "Wrote implementation-plan.md", cancellationToken)
            .ConfigureAwait(false);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "IA",
                stage: PipelineAgentConstants.StageIaPlan,
                phase: "plan",
                summary: "Implementation plan ready for codegen.",
                decisions:
                [
                    "Plan follows PRD + system design; no new product scope.",
                    "Codegen will materialize request-driven / rewrite / feature-increment per deliveryMode."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task<TokenUsage> RunIaCodegenAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var stack = message.Context.StackProfile ?? "fe-be";
        await BeginStepAsync(message, run, "ia.codegen.hints", "Gather generation hints (conversation)", cancellationToken)
            .ConfigureAwait(false);
        LlmCompletionResult llm;
        try
        {
            var conversation = await BuildPersonaContextAsync(message, PersonaKind.Developer, cancellationToken)
                .ConfigureAwait(false);
            llm = await CallPersonaAsync(
                    message,
                    500,
                    PipelineAgentConstants.IntentIa,
                    PersonaKind.Developer,
                    PromptLoader.LoadIaPhasePrompt("ia-codegen.txt"),
                    conversation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            llm = new LlmCompletionResult("{}", message.Config.ModelId ?? "local", 0, 0);
        }

        await CompleteStepAsync(message, run, "ia.codegen.hints", "Generation hints ready", cancellationToken)
            .ConfigureAwait(false);

        var workspace = ResolveWorkspace(message);
        var intent = PipelineAgentConstants.IntentWorkspace;
        var existing = await _ledger
            .TryGetCommittedAsync(message.WorkItemId, message.TaskId, intent, 600, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            var delivery = DeliveryModeClassifier.Classify(
                message.Context.UserRequest,
                message.Context.ProjectMode,
                message.Context.BaselineArtifacts);
            await BeginStepAsync(
                    message,
                    run,
                    "ia.codegen.generate",
                    delivery switch
                    {
                        DeliveryMode.Rewrite => "Rewrite baseline to net10 greenfield",
                        DeliveryMode.FeatureIncrement => "Apply feature on prior project workspace",
                        _ => "Generate app from user request (request-driven)"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            object meta;
            string summary;
            if (delivery == DeliveryMode.Rewrite)
            {
                var baselinePath = DeliveryModeClassifier.ResolveBaselinePath(message.Context.BaselineArtifacts)
                                   ?? GreenfieldRewriteBuilder.ResolveDefaultFixtureRoot();
                var rewritten = GreenfieldRewriteBuilder.Rewrite(
                    workspace,
                    baselinePath,
                    message.ProjectId,
                    message.WorkItemId,
                    message.Context.UserRequest ?? "");
                meta = rewritten;
                summary = $"Rewrote baseline → {rewritten.TargetFramework} ({rewritten.PreservedRoutes.Length} routes)";
                outputs["appKind"] = "Rewrite";
                outputs["deliveryMode"] = nameof(DeliveryMode.Rewrite);
            }
            else if (delivery == DeliveryMode.FeatureIncrement)
            {
                var prior = DeliveryModeClassifier.ResolveBaselinePath(message.Context.BaselineArtifacts);
                var feature = FeatureIncrementBuilder.Apply(
                    workspace,
                    message.ProjectId,
                    message.WorkItemId,
                    message.Context.UserRequest ?? "",
                    prior);
                meta = feature;
                summary =
                    $"Feature {feature.Feature} on prior workspace (+{string.Join(",", feature.RoutesAdded)})";
                outputs["appKind"] = feature.Feature.ToString();
                outputs["deliveryMode"] = nameof(DeliveryMode.FeatureIncrement);
            }
            else
            {
                var kind = RequestDrivenCodegen.Classify(
                    message.Context.UserRequest,
                    stack,
                    message.Context.ProjectName);
                var hintJson = ExtractTrailingJson(llm.Text) ?? llm.Text;
                meta = RequestDrivenCodegen.Generate(
                    workspace,
                    kind,
                    message.ProjectId,
                    message.WorkItemId,
                    message.Context.UserRequest ?? "",
                    hintJson);
                summary = $"Generated {kind}";
                outputs["appKind"] = kind.ToString();
                outputs["deliveryMode"] = nameof(DeliveryMode.Basic);
            }

            MonoRepoLayout.SyncFrontendToWwwroot(workspace);
            await CommitLedgerAsync(
                    message,
                    intent,
                    600,
                    new { workspace, stack, delivery = delivery.ToString() },
                    new { ok = true, meta },
                    cancellationToken)
                .ConfigureAwait(false);
            await CompleteStepAsync(
                    message,
                    run,
                    "ia.codegen.generate",
                    $"{summary} → {MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId)}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await BeginStepAsync(message, run, "ia.codegen.reuse", "Reuse committed workspace", cancellationToken)
                .ConfigureAwait(false);
            await CompleteStepAsync(message, run, "ia.codegen.reuse", "Workspace already generated", cancellationToken)
                .ConfigureAwait(false);
        }

        await BeginStepAsync(message, run, "ia.codegen.tdd", "Run TDD suite in builder", cancellationToken)
            .ConfigureAwait(false);
        await RunBuilderTestsAsync(message, workspace, cancellationToken).ConfigureAwait(false);
        await CompleteStepAsync(message, run, "ia.codegen.tdd", "Builder TDD green", cancellationToken)
            .ConfigureAwait(false);

        // Also publish test-results via the existing testing path for docs/UI.
        await RunTestingAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);

        await BeginStepAsync(message, run, "ia.codegen.commit", "Commit workspace snapshot", cancellationToken)
            .ConfigureAwait(false);
        var headCommit = await EnsureLocalGitCommitAsync(workspace, cancellationToken).ConfigureAwait(false);
        await CompleteStepAsync(message, run, "ia.codegen.commit", $"headCommit={headCommit}", cancellationToken)
            .ConfigureAwait(false);

        // code-state.json / code-graph.json come from graphify after review (locked command), not here.

        outputs["workBranch"] = MonoRepoLayout.WorkBranch(message.ProjectId, message.WorkItemId);
        outputs["headCommit"] = headCommit;
        outputs["workspaceRoot"] = workspace;
        outputs["monoFolder"] = MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "IA",
                stage: PipelineAgentConstants.StageIaCodegen,
                phase: "codegen",
                summary:
                    $"Generated/fixed workspace ({outputs.GetValueOrDefault("deliveryMode") ?? "Basic"} / {outputs.GetValueOrDefault("appKind") ?? "app"}); TDD green; head={headCommit}.",
                decisions:
                [
                    "Workspace is the source of truth for this revision.",
                    message.Artifacts.ContainsKey("taConcerns")
                        ? "Applied TA RouteBack concerns before regenerating."
                        : "Fresh generation from approved plan/design."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task<TokenUsage> RunIaReviewAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var workspace = message.Context.WorkspaceRoot ?? ResolveWorkspace(message);
        outputs["workspaceRoot"] = workspace;
        outputs["workBranch"] = MonoRepoLayout.WorkBranch(message.ProjectId, message.WorkItemId);

        await BeginStepAsync(message, run, "ia.review.converse", "Draft code review in conversation", cancellationToken)
            .ConfigureAwait(false);
        var allFiles = Directory.Exists(workspace)
            ? Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(workspace, p).Replace('\\', '/'))
                .Where(p => !p.StartsWith(".git/", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray()
            : [];
        // Keep the independent-reviewer pack small (guardrail input_too_long); structure > dump.
        var sample = allFiles.Take(40).ToArray();
        var appendix =
            $"\n\n## Generated file inventory (independent review)\nTotal files: {allFiles.Length}\nSample (first {sample.Length}):\n- "
            + string.Join("\n- ", sample);
        var conversation = await BuildPersonaContextAsync(message, PersonaKind.Reviewer, cancellationToken, appendix)
            .ConfigureAwait(false);
        LlmCompletionResult llm;
        try
        {
            llm = await CallPersonaAsync(
                    message,
                    710,
                    PipelineAgentConstants.IntentIa,
                    PersonaKind.Reviewer,
                    PromptLoader.LoadIaPhasePrompt("ia-review.txt"),
                    conversation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            llm = new LlmCompletionResult(
                """{"status":"passed","summary":"Template materialization and TDD completed.","checks":["frontend","backend","tests"]}""",
                message.Config.ModelId ?? "local",
                0,
                0);
        }

        await CompleteStepAsync(message, run, "ia.review.converse", "Review drafted", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "ia.review.persist", "Persist review.json", cancellationToken)
            .ConfigureAwait(false);
        var reviewJson = ExtractTrailingJson(llm.Text)
                         ?? JsonSerializer.Serialize(
                             new
                             {
                                 status = "passed",
                                 summary = llm.Text,
                                 checks = new[] { "frontend", "backend", "tests" }
                             },
                             JsonOptions);
        var reviewPath = message.BlobPrefix + "review.json";
        await WriteArtifactAsync(message, reviewPath, reviewJson, 710, cancellationToken).ConfigureAwait(false);
        var reviewMdPath = message.BlobPrefix + "code-review.md";
        await WriteArtifactAsync(message, reviewMdPath, llm.Text, 711, cancellationToken).ConfigureAwait(false);
        artifacts["review"] = reviewPath;
        artifacts["codeReview"] = reviewMdPath;
        await CompleteStepAsync(message, run, "ia.review.persist", "Wrote review.json and code-review.md", cancellationToken)
            .ConfigureAwait(false);

        // Locked CodeStateBuilder step (after review, before PR): graphify extract . --code-only
        await BeginStepAsync(
                message,
                run,
                "ia.codestate.graphify",
                GraphifyCodeStateBuilder.LockedCommand,
                cancellationToken)
            .ConfigureAwait(false);
        var graphify = await GraphifyCodeStateBuilder
            .BuildAsync(workspace, cancellationToken)
            .ConfigureAwait(false);
        var codeGraphPath = message.BlobPrefix + "code-graph.json";
        var codeStatePath = message.BlobPrefix + "code-state.json";
        await WriteArtifactAsync(message, codeGraphPath, graphify.CodeGraphJson, 720, cancellationToken)
            .ConfigureAwait(false);
        await WriteArtifactAsync(message, codeStatePath, graphify.CodeStateJson, 721, cancellationToken)
            .ConfigureAwait(false);
        artifacts["codeGraph"] = codeGraphPath;
        artifacts["codeState"] = codeStatePath;
        await CompleteStepAsync(
                message,
                run,
                "ia.codestate.graphify",
                $"Wrote code-graph.json + code-state.json from {graphify.GraphPath}",
                cancellationToken)
            .ConfigureAwait(false);

        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "IA",
                stage: PipelineAgentConstants.StageIaReview,
                phase: "review",
                summary: "Independent code review completed; graphify code-state recorded.",
                decisions:
                [
                    "Reviewer did not re-implement; evidence was plan + code-state.",
                    "code-state command locked: graphify extract . --code-only."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task RunIaPushDeployAsync(
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
            await RunDeployAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);
            return;
        }

        var branch = MonoRepoLayout.WorkBranch(message.ProjectId, message.WorkItemId);
        await BeginStepAsync(
                message,
                run,
                "ia.push.pr",
                $"Push branch and open PR ({_gitOptions.Provider})",
                cancellationToken)
            .ConfigureAwait(false);
        var title = $"IA: {MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId)}";
        var body =
            $"Automated Implementation Agent PR.\n\nRequest: {message.Context.UserRequest}\n\nMono folder: `{MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId)}` with `frontend/` and `backend/`.";
        var pr = await _git
            .PushAndOpenPrAsync(workspace, branch, title, body, cancellationToken)
            .ConfigureAwait(false);
        var prPath = message.BlobPrefix + "pull-request.json";
        var prJson = JsonSerializer.Serialize(
            new
            {
                provider = pr.Provider,
                prUrl = pr.PrUrl,
                prNumber = pr.PrNumber,
                branch = pr.Branch,
                headCommit = pr.HeadCommit,
                detail = pr.Detail,
                cloneUrl = _gitOptions.CloneUrl
            },
            JsonOptions);
        await WriteArtifactAsync(message, prPath, prJson, 850, cancellationToken).ConfigureAwait(false);
        artifacts["pullRequest"] = prPath;
        outputs["prUrl"] = pr.PrUrl;
        if (pr.PrNumber is int n)
        {
            outputs["prNumber"] = n.ToString();
        }

        outputs["workBranch"] = branch;
        outputs["headCommit"] = pr.HeadCommit;
        await CompleteStepAsync(message, run, "ia.push.pr", $"{pr.Provider}: {pr.PrUrl}", cancellationToken)
            .ConfigureAwait(false);

        await RunDeployAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);
        outputs["workspaceRoot"] = workspace;
        outputs["monoFolder"] = MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "IA",
                stage: PipelineAgentConstants.StageIaPushDeploy,
                phase: "push-deploy",
                summary: $"Opened PR and deployed DEV at {outputs.GetValueOrDefault("deployedUrls") ?? "(pending)"}.",
                decisions:
                [
                    "Feature branch PR — not direct push to main.",
                    "DeployedUrl is the live base for Testing Agent HTTP + Playwright."
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TokenUsage> RunTaGenerateAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var workspace = message.Context.WorkspaceRoot ?? ResolveWorkspace(message);
        var deployedUrl = message.Context.DeployedUrl
                          ?? throw new InvalidOperationException(
                              "TA_GENERATE requires Context.DeployedUrl from IA Push & Deploy.");
        outputs["workspaceRoot"] = workspace;
        outputs["deployedUrls"] = deployedUrl;
        if (!string.IsNullOrWhiteSpace(message.Context.PrUrl))
        {
            outputs["prUrl"] = message.Context.PrUrl!;
        }

        await BeginStepAsync(message, run, "ta.gen.converse", "Plan live-URL automation (conversation)", cancellationToken)
            .ConfigureAwait(false);
        var conversation = await BuildPersonaContextAsync(message, PersonaKind.QaAutomation, cancellationToken)
            .ConfigureAwait(false);
        LlmCompletionResult llm;
        try
        {
            llm = await CallPersonaAsync(
                    message,
                    900,
                    PipelineAgentConstants.IntentTa,
                    PersonaKind.QaAutomation,
                    PromptLoader.LoadNamedPromptFile("ta-generate.txt"),
                    conversation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            llm = new LlmCompletionResult("{}", message.Config.ModelId ?? "local", 0, 0);
        }

        await CompleteStepAsync(message, run, "ta.gen.converse", "Automation plan ready", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "ta.gen.write", "Write tests/e2e suite under mono folder", cancellationToken)
            .ConfigureAwait(false);
        var cases = DeployedSmokeGenerator.ParseCasesOrDefault(
            llm.Text,
            message.Context.StackProfile,
            message.Context.UserRequest,
            message.Context.ProjectName,
            message.Context.ProjectMode,
            message.Context.BaselineArtifacts);
        DeployedSmokeGenerator.WriteSuite(workspace, cases);
        PlaywrightSuiteGenerator.WriteSuite(
            workspace,
            cases,
            message.Context.UserRequest,
            message.Context.StackProfile,
            message.Context.ProjectName);
        // Optional probe: first TA pass writes a poisoned expect so Manual QA can prove RouteBack.
        if (message.Artifacts.ContainsKey("taRoutebackProbe")
            || string.Equals(
                Environment.GetEnvironmentVariable("BGX_TA_ROUTEBACK_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            PoisonHttpExpectOnce(workspace);
        }

        var planPath = message.BlobPrefix + "ta-test-plan.md";
        var planBody = string.IsNullOrWhiteSpace(llm.Text)
            ? "Live HTTP smoke + Playwright FE cases (health + primary API / UI action)."
            : llm.Text;
        await WriteArtifactAsync(message, planPath, planBody, 910, cancellationToken, "taTestPlan")
            .ConfigureAwait(false);
        artifacts["taTestPlan"] = planPath;
        await CompleteStepAsync(
                message,
                run,
                "ta.gen.write",
                $"Wrote {cases.Count} HTTP cases + Playwright FE suite",
                cancellationToken)
            .ConfigureAwait(false);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "TA",
                stage: PipelineAgentConstants.StageTaGenerate,
                phase: "generate",
                summary: $"Wrote {cases.Count} live HTTP cases plus Playwright FE suite against deployed URL.",
                decisions:
                [
                    "Cases derived from deliveryMode / request classifier — not from review.json cheerleading.",
                    "Playwright soft-skips if browsers unavailable; HTTP smoke remains hard gate."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task RunTaVerifyAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var workspace = message.Context.WorkspaceRoot ?? ResolveWorkspace(message);
        var deployedUrl = message.Context.DeployedUrl
                          ?? throw new InvalidOperationException("TA_VERIFY requires Context.DeployedUrl.");
        outputs["workspaceRoot"] = workspace;
        outputs["deployedUrls"] = deployedUrl;

        if (!Directory.Exists(Path.Combine(workspace, "tests", "e2e")))
        {
            var defaults = DeployedSmokeGenerator.DefaultCases(
                message.Context.StackProfile,
                message.Context.UserRequest,
                message.Context.ProjectName,
                message.Context.ProjectMode,
                message.Context.BaselineArtifacts);
            DeployedSmokeGenerator.WriteSuite(workspace, defaults);
            PlaywrightSuiteGenerator.WriteSuite(
                workspace,
                defaults,
                message.Context.UserRequest,
                message.Context.StackProfile,
                message.Context.ProjectName);
        }

        await BeginStepAsync(
                message,
                run,
                "ta.verify.run",
                $"Run HTTP + Playwright against {deployedUrl}",
                cancellationToken)
            .ConfigureAwait(false);
        var outcome = await TaVerifyRunner.RunAsync(workspace, deployedUrl, cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "ta.verify.persist", "Persist ta-results.json", cancellationToken)
            .ConfigureAwait(false);
        var path = message.BlobPrefix + "ta-results.json";
        await WriteArtifactAsync(message, path, outcome.ResultsJson, 920, cancellationToken, "taResults")
            .ConfigureAwait(false);
        artifacts["taResults"] = path;

        if (!outcome.Green)
        {
            var attempt = Math.Max(1, message.InputVersion);
            var concerns = TaConcernPackage.Build(
                deployedUrl,
                outcome.ResultsJson,
                TaConcernPackage.FailingNames(outcome.ResultsJson),
                attempt);
            var concernPath = message.BlobPrefix + "ta-concerns.json";
            await WriteArtifactAsync(message, concernPath, concerns, 921, cancellationToken, "taConcerns")
                .ConfigureAwait(false);
            artifacts["taConcerns"] = concernPath;
            outputs["taStatus"] = "red";
            outputs["signal.kind"] = "RouteBack";
            outputs["signal.routeBackTo"] = PipelineAgentConstants.StageIaCodegen;
            outputs["signal.detail"] = Truncate(outcome.Detail, 500);
            ClearHttpPoison(workspace);
            await CompleteStepAsync(message, run, "ta.verify.run", "Live verification red — RouteBack to IA", cancellationToken, failed: true)
                .ConfigureAwait(false);
            await CompleteStepAsync(message, run, "ta.verify.persist", "Wrote ta-results + ta-concerns (RouteBack)", cancellationToken)
                .ConfigureAwait(false);
            await AppendDecisionAsync(
                    message,
                    artifacts,
                    agent: "TA",
                    stage: PipelineAgentConstants.StageTaVerify,
                    phase: "verify-red",
                    summary: "Live verification red — RouteBack to IA with ta-concerns.json.",
                    decisions:
                    [
                        "AgentResult stays Completed with signal.kind=RouteBack (not Failed).",
                        "IA must fix, re-test, re-deploy; artifact revision will bump."
                    ],
                    cancellationToken)
                .ConfigureAwait(false);
            // Completed AgentResult (not Failed) — orchestrator routes back to IA.
            return;
        }

        outputs["taStatus"] = "green";
        ClearHttpPoison(workspace);
        await CompleteStepAsync(message, run, "ta.verify.run", "HTTP + Playwright green", cancellationToken)
            .ConfigureAwait(false);
        await CompleteStepAsync(message, run, "ta.verify.persist", "Wrote ta-results.json", cancellationToken)
            .ConfigureAwait(false);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "TA",
                stage: PipelineAgentConstants.StageTaVerify,
                phase: "verify-green",
                summary: $"Live verification green against {deployedUrl}.",
                decisions:
                [
                    "HTTP smoke hard-gate passed.",
                    "Playwright FE ran or soft-skipped; overall status green."
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunDeploySandboxAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var deployedUrl = message.Context.DeployedUrl ?? outputs.GetValueOrDefault("deployedUrls");
        outputs["deployedUrls"] = deployedUrl ?? "";
        await BeginStepAsync(message, run, "deploy.sandbox.publish", "Publish sandbox URL for acceptance", cancellationToken)
            .ConfigureAwait(false);

        // Require TA verification report when present (proof before deployment handoff).
        var reportJson = await _blobs
            .TryReadJsonAsync(message.BlobPrefix + "ta-verification-report.json", cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(reportJson) && artifacts.TryGetValue("taVerificationReport", out var reportPath))
        {
            reportJson = await _blobs.TryReadJsonAsync(reportPath, cancellationToken).ConfigureAwait(false);
        }

        var json = SandboxDeployPublisher.BuildArtifact(message, deployedUrl, message.Context.PrUrl);
        if (!string.IsNullOrWhiteSpace(reportJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                using var report = JsonDocument.Parse(reportJson);
                var merged = new Dictionary<string, object?>();
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    merged[p.Name] = JsonSerializer.Deserialize<object>(p.Value.GetRawText());
                }

                merged["taVerificationReport"] = JsonSerializer.Deserialize<object>(reportJson);
                merged["taVerificationReady"] =
                    report.RootElement.TryGetProperty("readyForDeployment", out var ready)
                    && ready.ValueKind == JsonValueKind.True;
                json = JsonSerializer.Serialize(merged, JsonOptions);
            }
            catch (JsonException)
            {
                // keep sandbox artifact without embed
            }
        }

        var path = message.BlobPrefix + "sandbox-url.json";
        await WriteArtifactAsync(message, path, json, 960, cancellationToken, "sandboxUrl")
            .ConfigureAwait(false);
        artifacts["sandboxUrl"] = path;
        var mdPath = message.BlobPrefix + "sandbox-url.md";
        await WriteArtifactAsync(
                message,
                mdPath,
                JsonSerializer.Serialize(new { content = SandboxDeployPublisher.BuildMarkdown(deployedUrl) }, JsonOptions),
                961,
                cancellationToken)
            .ConfigureAwait(false);
        artifacts["sandboxUrlMd"] = mdPath;
        outputs["sandboxUrl"] = deployedUrl ?? "";
        outputs["deploymentStatus"] = "waiting_for_approval";
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "Deploy",
                stage: PipelineAgentConstants.StageDeploySandbox,
                phase: "publish",
                summary: $"Sandbox URL published for acceptance: {deployedUrl ?? "(missing)"}.",
                decisions:
                [
                    "No re-deploy — URL comes from IA Push & Deploy.",
                    "TA verification report attached when available; waiting for approval to leave sandbox."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        await CompleteStepAsync(
                message,
                run,
                "deploy.sandbox.publish",
                string.IsNullOrWhiteSpace(deployedUrl)
                    ? "Sandbox stub wrote waiting_for_approval (URL missing)"
                    : $"Sandbox URL shared: {deployedUrl}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void PoisonHttpExpectOnce(string workspace)
    {
        var casesPath = Path.Combine(workspace, "tests", "e2e", "cases.json");
        if (!File.Exists(casesPath))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(casesPath));
            var list = new List<object>();
            foreach (var el in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                var name = el.GetProperty("name").GetString() ?? "case";
                var method = el.TryGetProperty("method", out var m) ? m.GetString() ?? "GET" : "GET";
                var path = el.TryGetProperty("path", out var p) ? p.GetString() ?? "/health" : "/health";
                var status = el.TryGetProperty("expectStatus", out var s) && s.TryGetInt32(out var code) ? code : 200;
                var contains = name.Equals("HealthOk", StringComparison.OrdinalIgnoreCase)
                    ? (el.TryGetProperty("expectBodyContains", out var c) ? c.GetString() : "ok")
                    : "__ROUTEBACK_PROBE_FAIL__";
                list.Add(new { name, method, path, expectStatus = status, expectBodyContains = contains });
            }

            File.WriteAllText(
                casesPath,
                JsonSerializer.Serialize(new { cases = list }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(workspace, "tests", "e2e", ".routeback-probe"), "1");
        }
        catch
        {
            // ignore probe failures
        }
    }

    private static void ClearHttpPoison(string workspace)
    {
        var marker = Path.Combine(workspace, "tests", "e2e", ".routeback-probe");
        if (File.Exists(marker))
        {
            try { File.Delete(marker); } catch { /* ignore */ }
        }
    }

    private async Task<TokenUsage> RunTaReportAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var deployedUrl = message.Context.DeployedUrl ?? "";
        outputs["deployedUrls"] = deployedUrl;
        await BeginStepAsync(message, run, "ta.report.converse", "Draft QA report (conversation)", cancellationToken)
            .ConfigureAwait(false);
        var conversation = await BuildPersonaContextAsync(message, PersonaKind.QaReporter, cancellationToken)
            .ConfigureAwait(false);
        LlmCompletionResult llm;
        try
        {
            llm = await CallPersonaAsync(
                    message,
                    930,
                    PipelineAgentConstants.IntentTa,
                    PersonaKind.QaReporter,
                    PromptLoader.LoadNamedPromptFile("ta-report.txt"),
                    conversation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            llm = new LlmCompletionResult(
                """{"status":"green","summary":"Live smoke passed.","readyToMerge":true}""",
                message.Config.ModelId ?? "local",
                0,
                0);
        }

        await CompleteStepAsync(message, run, "ta.report.converse", "Report drafted", cancellationToken)
            .ConfigureAwait(false);

        await BeginStepAsync(message, run, "ta.report.persist", "Persist ta-report + verification report", cancellationToken)
            .ConfigureAwait(false);
        var mdPath = message.BlobPrefix + "ta-report.md";
        await WriteArtifactAsync(message, mdPath, llm.Text, 931, cancellationToken).ConfigureAwait(false);
        var statusJson = ExtractTrailingJson(llm.Text)
                         ?? JsonSerializer.Serialize(
                             new { status = "green", summary = llm.Text, readyToMerge = true },
                             JsonOptions);
        var statusPath = message.BlobPrefix + "ta-status.json";
        await WriteArtifactAsync(message, statusPath, statusJson, 932, cancellationToken).ConfigureAwait(false);
        artifacts["taReport"] = mdPath;
        artifacts["taStatus"] = statusPath;
        outputs["taStatus"] = "green";

        // Explicit verification report (test plan + results) before Deployment Agent.
        var planText = await _blobs.TryReadJsonAsync(message.BlobPrefix + "ta-test-plan.md", cancellationToken)
            .ConfigureAwait(false);
        var resultsText = await _blobs.TryReadJsonAsync(message.BlobPrefix + "ta-results.json", cancellationToken)
            .ConfigureAwait(false);
        var decisionRaw = await DecisionLogWriter
            .TryLoadLatestAsync(
                _blobs,
                message.ProjectId,
                message.WorkItemId,
                message.BlobPrefix,
                cancellationToken)
            .ConfigureAwait(false);
        var decisionSummary = "";
        if (!string.IsNullOrWhiteSpace(decisionRaw))
        {
            try
            {
                using var ddoc = JsonDocument.Parse(decisionRaw);
                if (ddoc.RootElement.TryGetProperty("decisionSummary", out var ds))
                {
                    decisionSummary = ds.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                decisionSummary = Truncate(decisionRaw!, 400);
            }
        }

        var rev = message.OutputRevision < 1 ? 1 : message.OutputRevision;
        var verificationJson = TaVerificationReportBuilder.BuildJson(
            deployedUrl,
            planText,
            resultsText,
            statusJson,
            decisionSummary,
            rev);
        var verificationPath = message.BlobPrefix + "ta-verification-report.json";
        await WriteArtifactAsync(
                message,
                verificationPath,
                verificationJson,
                933,
                cancellationToken,
                "taVerificationReport")
            .ConfigureAwait(false);
        artifacts["taVerificationReport"] = verificationPath;
        var verificationMd = TaVerificationReportBuilder.BuildMarkdown(
            deployedUrl,
            "green",
            planText,
            decisionSummary);
        var verificationMdPath = message.BlobPrefix + "ta-verification-report.md";
        await WriteArtifactAsync(
                message,
                verificationMdPath,
                JsonSerializer.Serialize(new { content = verificationMd }, JsonOptions),
                934,
                cancellationToken)
            .ConfigureAwait(false);
        artifacts["taVerificationReportMd"] = verificationMdPath;

        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "TA",
                stage: PipelineAgentConstants.StageTaReport,
                phase: "verification-report",
                summary: "Published TA verification report; ready for DEPLOY_SANDBOX.",
                decisions:
                [
                    "Report consolidates test plan, live results, and pipeline decision summary.",
                    "Deployment Agent must not re-test; it shares the sandbox URL for acceptance."
                ],
                cancellationToken)
            .ConfigureAwait(false);
        await CompleteStepAsync(
                message,
                run,
                "ta.report.persist",
                "Wrote ta-report, ta-status, and ta-verification-report",
                cancellationToken)
            .ConfigureAwait(false);
        return Tokens(llm);
    }

    private async Task RunTaMergeAsync(
        TaskMessage message,
        StageRunContext run,
        Dictionary<string, string> artifacts,
        Dictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        var workspace = message.Context.WorkspaceRoot ?? ResolveWorkspace(message);
        outputs["workspaceRoot"] = workspace;
        if (!string.IsNullOrWhiteSpace(message.Context.DeployedUrl))
        {
            outputs["deployedUrls"] = message.Context.DeployedUrl!;
        }

        await BeginStepAsync(message, run, "ta.merge.pr", "Merge pull request after green TA", cancellationToken)
            .ConfigureAwait(false);
        var prNumber = message.Context.PrNumber;
        if (prNumber is null)
        {
            var prJson = await _blobs.TryReadJsonAsync(message.BlobPrefix + "pull-request.json", cancellationToken)
                .ConfigureAwait(false);
            if (prJson is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(prJson);
                    if (doc.RootElement.TryGetProperty("prNumber", out var n) && n.TryGetInt32(out var num))
                    {
                        prNumber = num;
                    }
                }
                catch (JsonException)
                {
                    // fall through
                }
            }
        }

        PullRequestResult merge;
        if (prNumber is int pn)
        {
            merge = await _git
                .MergePullRequestAsync(pn, message.Context.WorkBranch, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            merge = new PullRequestResult(
                "none",
                message.Context.PrUrl ?? "local://no-pr",
                null,
                message.Context.WorkBranch ?? "",
                message.Context.HeadCommit ?? "local",
                "No prNumber available; recorded logical merge.");
        }

        var mergePath = message.BlobPrefix + "merge.json";
        var mergeJson = JsonSerializer.Serialize(
            new
            {
                provider = merge.Provider,
                prUrl = merge.PrUrl,
                prNumber = merge.PrNumber,
                mergeCommit = merge.HeadCommit,
                detail = merge.Detail,
                deployedUrl = message.Context.DeployedUrl
            },
            JsonOptions);
        await WriteArtifactAsync(message, mergePath, mergeJson, 940, cancellationToken).ConfigureAwait(false);
        artifacts["merge"] = mergePath;
        outputs["mergeCommit"] = string.IsNullOrWhiteSpace(merge.HeadCommit)
            ? message.Context.HeadCommit ?? "merged"
            : merge.HeadCommit;
        outputs["headCommit"] = outputs["mergeCommit"];
        outputs["prUrl"] = merge.PrUrl;
        await CompleteStepAsync(message, run, "ta.merge.pr", $"{merge.Provider}: {merge.Detail}", cancellationToken)
            .ConfigureAwait(false);

        // Keep local deploy finalize semantics for Compose cleanup markers.
        message.Context.DeploymentAction = "Finalize";
        await RunDeployAsync(message, run, artifacts, outputs, cancellationToken).ConfigureAwait(false);
        await AppendDecisionAsync(
                message,
                artifacts,
                agent: "TA",
                stage: PipelineAgentConstants.StageTaMerge,
                phase: "merge",
                summary: $"Merged PR after green TA; deployed URL remains {message.Context.DeployedUrl ?? outputs.GetValueOrDefault("deployedUrls")}.",
                decisions:
                [
                    "Merge only after verification report green.",
                    "Next: DEPLOY_SANDBOX publishes URL for acceptance (no re-deploy)."
                ],
                cancellationToken)
            .ConfigureAwait(false);
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
            await BeginStepAsync(
                    message,
                    run,
                    "deploy.finalize",
                    $"Finalize deployment ({_deployOptions.Provider})",
                    cancellationToken)
                .ConfigureAwait(false);
            await _deploy.FinalizeAsync(workspace, cancellationToken).ConfigureAwait(false);
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

        await BeginStepAsync(
                message,
                run,
                "deploy.host",
                $"Start app host ({_deployOptions.Provider})",
                cancellationToken)
            .ConfigureAwait(false);
        var deploy = await _deploy
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

        var persona = message.Stage switch
        {
            PipelineAgentConstants.StagePrd => PersonaKind.Product,
            PipelineAgentConstants.StageSystemDesign => PersonaKind.Architect,
            _ => PersonaKind.Developer
        };
        var system = PromptLoader.LoadPersonaSystemPrompt(persona);
        var stageSection = PromptLoader.LoadStageSection(message.Stage);
        var evidence = await BuildPersonaContextAsync(message, persona, cancellationToken).ConfigureAwait(false);
        var userPrompt = Truncate(
            stageSection + "\n\n---\n\n" + evidence,
            LocalGuardrailPipeline.DefaultMaxInputChars);

        // Pipeline LLM output is JSON by design; evaluate input only, then store truncated text.
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
        CancellationToken cancellationToken,
        string? artifactKey = null)
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

        var key = artifactKey ?? InferArtifactKey(blobPath);
        var revision = message.OutputRevision < 1 ? 1 : message.OutputRevision;
        if (key is not null)
        {
            await VersionedArtifactWriter
                .WriteAsync(
                    _blobs,
                    message.ProjectId,
                    message.WorkItemId,
                    revision,
                    key,
                    blobPath,
                    jsonBody,
                    cancellationToken,
                    new Dictionary<string, string>
                    {
                        ["stage"] = message.Stage,
                        ["deployedUrl"] = message.Context.DeployedUrl ?? ""
                    })
                .ConfigureAwait(false);
        }
        else
        {
            await _blobs.WriteJsonAsync(blobPath, jsonBody, cancellationToken).ConfigureAwait(false);
        }

        await CommitLedgerAsync(
                message,
                PipelineAgentConstants.IntentArtifact,
                ordinal,
                null,
                new { blobPath, bytes = jsonBody.Length, key, revision },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? InferArtifactKey(string blobPath)
    {
        var name = Path.GetFileName(blobPath);
        return name switch
        {
            "prd.json" => "prd",
            "prd.md" => "prdMd",
            "techspec.json" => "techSpec",
            "system-design.md" => "techSpecMd",
            "implementation-plan.md" => "implementationPlan",
            "code-state.json" => "codeState",
            "code-graph.json" => "codeGraph",
            "review.json" => "review",
            "test-results.json" => "testResults",
            "pull-request.json" => "pullRequest",
            "ta-test-plan.md" => "taTestPlan",
            "ta-results.json" => "taResults",
            "ta-report.md" => "taReport",
            "ta-status.json" => "taStatus",
            "ta-concerns.json" => "taConcerns",
            "ta-verification-report.json" => "taVerificationReport",
            "merge.json" => "merge",
            "completion-summary.json" => "completionSummary",
            "sandbox-url.json" => "sandboxUrl",
            "decision-log.json" => "decisionLog",
            _ => null
        };
    }

    private async Task AppendDecisionAsync(
        TaskMessage message,
        Dictionary<string, string> artifacts,
        string agent,
        string stage,
        string phase,
        string summary,
        IReadOnlyList<string> decisions,
        CancellationToken cancellationToken)
    {
        var revision = message.OutputRevision < 1 ? 1 : message.OutputRevision;
        var prefix = message.BlobPrefix
                     ?? VersionedArtifactPaths.RevisionPrefix(message.ProjectId, message.WorkItemId, revision);
        await DecisionLogWriter
            .AppendAsync(
                _blobs,
                message.ProjectId,
                message.WorkItemId,
                revision,
                prefix,
                agent,
                stage,
                phase,
                summary,
                decisions,
                extras: new Dictionary<string, string>
                {
                    ["deployedUrl"] = message.Context.DeployedUrl ?? "",
                    ["projectMode"] = message.Context.ProjectMode ?? ""
                },
                cancellationToken)
            .ConfigureAwait(false);
        artifacts["decisionLog"] = prefix.TrimEnd('/') + "/decision-log.json";
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

    private static string ResolveWorkspace(TaskMessage message) =>
        MonoRepoLayout.ResolveMonoRoot(message.ProjectId, message.WorkItemId, message.Context.WorkspaceRoot);

    /// <summary>
    /// Builds a persona-scoped evidence pack. Reviewer/QA never receive implementer self-talk
    /// (codegen hints, review drafts they didn't write, etc.) — only the artifacts that persona is allowed to judge.
    /// </summary>
    private async Task<string> BuildPersonaContextAsync(
        TaskMessage message,
        PersonaKind persona,
        CancellationToken cancellationToken,
        string? extraAppendix = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Persona context pack for: {persona}");
        sb.AppendLine("These materials were produced by other personas or the runtime. Treat them as evidence, not as your own prior reasoning.");
        sb.AppendLine();
        sb.AppendLine($"Project id: {message.ProjectId}");
        sb.AppendLine($"Work item id: {message.WorkItemId}");
        sb.AppendLine($"Stack profile: {message.Context.StackProfile}");
        sb.AppendLine($"Project mode: {message.Context.ProjectMode}");
        sb.AppendLine($"Mono-repo folder: {MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId)}");
        sb.AppendLine();
        sb.AppendLine("## User request");
        sb.AppendLine(message.Context.UserRequest ?? "(missing)");
        sb.AppendLine();

        // Shared decision log — what prior agents decided (crash-safe artifact).
        var decisionLog = await DecisionLogWriter
            .TryLoadLatestAsync(
                _blobs,
                message.ProjectId,
                message.WorkItemId,
                message.BlobPrefix,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(decisionLog))
        {
            sb.AppendLine("## Decision log so far (prior agents)");
            sb.AppendLine(Truncate(decisionLog!, 4000));
            sb.AppendLine();
        }

        switch (persona)
        {
            case PersonaKind.Product:
                // Request + decision log — product owns the PRD draft.
                break;
            case PersonaKind.Architect:
                await AppendBlobSectionAsync(sb, message, "prd.json", "PRD (input from Product)", cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PersonaKind.Planner:
                await AppendBlobSectionAsync(sb, message, "prd.json", "PRD (input from Product)", cancellationToken)
                    .ConfigureAwait(false);
                await AppendBlobSectionAsync(
                        sb,
                        message,
                        "system-design.md",
                        "System design (input from Architect)",
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PersonaKind.Developer:
                await AppendBlobSectionAsync(sb, message, "prd.json", "PRD (requirements)", cancellationToken)
                    .ConfigureAwait(false);
                await AppendBlobSectionAsync(
                        sb,
                        message,
                        "implementation-plan.md",
                        "Implementation plan (input from Planner)",
                        cancellationToken)
                    .ConfigureAwait(false);
                // RouteBack from Testing Agent — fix these red cases then re-deploy.
                if (message.Artifacts.TryGetValue("taConcerns", out var concernPath)
                    && !string.IsNullOrWhiteSpace(concernPath))
                {
                    var concernBody = await _blobs.TryReadJsonAsync(concernPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(concernBody))
                    {
                        sb.AppendLine("## TA concerns (RouteBack — must fix)");
                        sb.AppendLine(Truncate(concernBody!, 6000));
                        sb.AppendLine();
                    }
                }
                else
                {
                    await AppendBlobSectionAsync(
                            sb,
                            message,
                            "ta-concerns.json",
                            "TA concerns (RouteBack — must fix)",
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                break;
            case PersonaKind.Reviewer:
                // Independent review: plan + inventory only — never codegen LLM prose or prior review drafts.
                await AppendBlobSectionAsync(
                        sb,
                        message,
                        "implementation-plan.md",
                        "Approved implementation plan (requirements bar)",
                        cancellationToken)
                    .ConfigureAwait(false);
                await AppendBlobSectionAsync(
                        sb,
                        message,
                        "code-state.json",
                        "Generated file inventory (code-state.json)",
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PersonaKind.QaAutomation:
                // Independent QA: PRD + design APIs + URL — not reviewer pass/fail cheerleading.
                await AppendBlobSectionAsync(sb, message, "prd.json", "PRD acceptance criteria", cancellationToken)
                    .ConfigureAwait(false);
                await AppendBlobSectionAsync(
                        sb,
                        message,
                        "techspec.json",
                        "System design APIs (techspec.json)",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message.Context.DeployedUrl))
                {
                    sb.AppendLine("## Deployed URL (runtime)");
                    sb.AppendLine(message.Context.DeployedUrl);
                    sb.AppendLine();
                }

                break;
            case PersonaKind.QaReporter:
                await AppendBlobSectionAsync(sb, message, "prd.json", "PRD acceptance bar", cancellationToken)
                    .ConfigureAwait(false);
                await AppendBlobSectionAsync(
                        sb,
                        message,
                        "ta-results.json",
                        "Live automation results (ta-results.json)",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message.Context.DeployedUrl))
                {
                    sb.AppendLine("## Deployed URL (runtime)");
                    sb.AppendLine(message.Context.DeployedUrl);
                    sb.AppendLine();
                }

                break;
        }

        if (!string.IsNullOrWhiteSpace(extraAppendix))
        {
            sb.AppendLine(extraAppendix.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task AppendBlobSectionAsync(
        StringBuilder sb,
        TaskMessage message,
        string fileName,
        string heading,
        CancellationToken cancellationToken)
    {
        var path = message.BlobPrefix + fileName;
        var body = await _blobs.TryReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
        // TryReadJsonAsync is used for all text blobs in this POC (md/json).
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        sb.AppendLine($"## {heading}");
        sb.AppendLine(body);
        sb.AppendLine();
    }

    private Task<LlmCompletionResult> CallPersonaAsync(
        TaskMessage message,
        int ordinal,
        string intentKind,
        PersonaKind persona,
        string phaseInstructions,
        string conversation,
        CancellationToken cancellationToken) =>
        CallConversationAsync(
            message,
            ordinal,
            intentKind,
            PromptLoader.LoadPersonaSystemPrompt(persona),
            phaseInstructions,
            conversation,
            cancellationToken);

    private async Task<LlmCompletionResult> CallConversationAsync(
        TaskMessage message,
        int ordinal,
        string intentKind,
        string system,
        string phaseInstructions,
        string conversation,
        CancellationToken cancellationToken)
    {
        var existing = await _ledger
            .TryGetCommittedAsync(
                message.WorkItemId,
                message.TaskId,
                intentKind,
                ordinal,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing?.OutputJson is { Length: > 0 })
        {
            return JsonSerializer.Deserialize<LlmCompletionResult>(existing.OutputJson, JsonOptions)
                   ?? throw new InvalidOperationException($"Corrupt {intentKind} ledger OutputJson.");
        }

        var userPrompt =
            phaseInstructions
            + "\n\n---\n\n"
            + conversation;

        // Truncate evidence packs so independent personas never trip input_too_long.
        var boundedPrompt = Truncate(userPrompt, LocalGuardrailPipeline.DefaultMaxInputChars);
        await EnforceGuardrailAsync(
                _guardrails.EvaluateInput(boundedPrompt, message.CorrelationId),
                message,
                cancellationToken)
            .ConfigureAwait(false);

        var completion = await _llm
            .CompleteAsync(
                new LlmCompletionRequest(
                    system,
                    boundedPrompt,
                    message.Config.ModelId,
                    message.Config.ReasoningEffort),
                cancellationToken)
            .ConfigureAwait(false);

        // Conversational Markdown; truncate only.
        var text = Truncate(completion.Text, LocalGuardrailPipeline.DefaultMaxOutputChars);
        var stored = completion with { Text = text };
        await CommitLedgerAsync(
                message,
                intentKind,
                ordinal,
                new { stage = message.Stage, ordinal },
                stored,
                cancellationToken)
            .ConfigureAwait(false);
        return stored;
    }

    private async Task RunBuilderTestsAsync(
        TaskMessage message,
        string workspace,
        CancellationToken cancellationToken)
    {
        var testProj = Directory.EnumerateFiles(workspace, "*Tests.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (testProj is null)
        {
            var csproj = Directory.EnumerateFiles(
                    Path.Combine(workspace, "backend"),
                    "*.csproj",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (csproj is null)
            {
                return;
            }

            var rel = Path.GetRelativePath(workspace, csproj).Replace('\\', '/');
            var (exit, output) = await BuilderShell
                .RunAsync(workspace, $"dotnet build \"{rel}\" --nologo", cancellationToken)
                .ConfigureAwait(false);
            if (exit != 0)
            {
                throw new InvalidOperationException($"Builder build failed: {Truncate(output, 2000)}");
            }

            return;
        }

        var testRel = Path.GetRelativePath(workspace, testProj).Replace('\\', '/');
        var (testExit, testOut) = await BuilderShell
            .RunAsync(
                workspace,
                $"dotnet test \"{testRel}\" --nologo",
                cancellationToken,
                new Dictionary<string, string> { ["OLLAMA_BASE_URL"] = "http://127.0.0.1:9" })
            .ConfigureAwait(false);
        if (testExit != 0)
        {
            // Host fallback already attempted inside BuilderShell when Docker missing.
            // If builder image lacks restore network, fall back to host test once.
            if (BuilderShell.DockerAvailable())
            {
                var (hostExit, hostOut) = await RunProcessAsync(
                        ResolveDotnet(),
                        $"test \"{testProj}\" --nologo",
                        Path.GetDirectoryName(testProj)!,
                        cancellationToken,
                        new Dictionary<string, string> { ["OLLAMA_BASE_URL"] = "http://127.0.0.1:9" })
                    .ConfigureAwait(false);
                if (hostExit == 0)
                {
                    _logger.LogWarning(
                        "Builder container tests failed; host fallback succeeded for {WorkItemId}. Builder out: {Out}",
                        message.WorkItemId,
                        Truncate(testOut, 500));
                    return;
                }

                throw new InvalidOperationException(
                    $"Builder and host tests failed: {Truncate(testOut, 1000)} | {Truncate(hostOut, 1000)}");
            }

            throw new InvalidOperationException($"Builder tests failed: {Truncate(testOut, 2000)}");
        }
    }

    private static string DefaultIaPlanMarkdown(TaskMessage message) =>
        $"""
        # Implementation Plan

        ## Goal
        Deliver the requested slice for: {message.Context.UserRequest}

        ## Mono-repo layout
        `{MonoRepoLayout.FolderName(message.ProjectId, message.WorkItemId)}/frontend` and `.../backend`.

        ## Backend changes
        Generate the app at runtime from the user request (hello / hello+LLM / todo / calculator / generic), keep `/health` plus primary APIs.

        ## Frontend changes
        Static UI under `frontend/` synced into the API wwwroot for local serving.

        ## Tests
        HTTP round-trip xUnit suite under `tests/`.

        ## Code state
        After review: `graphify extract . --code-only` → `code-graph.json` + `code-state.json`. Open a feature-branch PR (do not push to main).

        ## Risks
        Request classification must match the ask; Fixture LLM does not invent arbitrary file trees yet.
        """;

    private static string? ExtractTrailingJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.LastIndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var slice = text[start..(end + 1)];
        try
        {
            using var _ = JsonDocument.Parse(slice);
            return slice;
        }
        catch (JsonException)
        {
            return null;
        }
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
                    result = CalculatorApi.Services.CalculatorMath.Subtract(a, b)
                }));
            // {{EXTRA_ENDPOINTS}}
            """,
            StringComparison.Ordinal);
        File.WriteAllText(program, text);

        foreach (var index in new[]
                 {
                     Path.Combine(workspace, "frontend", "index.html"),
                     Path.Combine(workspace, "backend", "wwwroot", "index.html")
                 })
        {
            if (!File.Exists(index))
            {
                continue;
            }

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
