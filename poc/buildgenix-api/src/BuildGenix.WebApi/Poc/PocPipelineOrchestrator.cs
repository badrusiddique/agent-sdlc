using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using BuildGenix.Data;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using BuildGenix.Data.Repositories;
using Microsoft.Extensions.Options;

namespace BuildGenix.WebApi.Poc;

/// <summary>
/// Pre-phase-0 orchestrator. Sync /run keeps auto-approve for scripts.
/// Interactive /start pauses at PRD, SYSTEM_DESIGN, and post-DEPLOY for human approve.
/// </summary>
public sealed class PocPipelineOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] StageOrder =
    [
        "PRD",
        "SYSTEM_DESIGN",
        "IMPLEMENTATION",
        "TESTING",
        "DEPLOY_DEV"
    ];

    private static readonly (string Id, string Label)[] StageStrip =
    [
        ("INTAKE", "Intake"),
        ("PRD", "PRD"),
        ("SYSTEM_DESIGN", "System Design"),
        ("IMPLEMENTATION", "Implementation"),
        ("TESTING", "Testing"),
        ("DEPLOY_DEV", "Deploy"),
        ("DONE", "Done")
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProjectRepository _projects;
    private readonly IWorkItemRepository _workItems;
    private readonly ITableClientFactory _tables;
    private readonly PocPipelineOptions _options;
    private readonly ILogger<PocPipelineOrchestrator> _logger;
    private readonly Dictionary<string, WorkItemEntity> _memoryItems = new(StringComparer.Ordinal);
    private readonly List<WorkItemEventEntity> _memoryEvents = [];
    private readonly List<ApprovalEntity> _memoryApprovals = [];
    private readonly Dictionary<string, ArtifactEntity> _memoryArtifacts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PipelineSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _eventLock = new();

    public PocPipelineOrchestrator(
        IHttpClientFactory httpClientFactory,
        IProjectRepository projects,
        IWorkItemRepository workItems,
        ITableClientFactory tables,
        IOptions<PocPipelineOptions> options,
        ILogger<PocPipelineOrchestrator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _projects = projects;
        _workItems = workItems;
        _tables = tables;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PocPipelineRunResult> RunAsync(
        PocPipelineStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
        await ExecutePipelineAsync(session, request, cancellationToken).ConfigureAwait(false);
        return ToRunResult(session);
    }

    public async Task<PocPipelineStartResult> StartAsync(
        PocPipelineStartRequest request,
        CancellationToken cancellationToken = default)
    {
        request.AutoApprove = false;
        // Do not link to the HTTP request token — the run outlives the start response.
        var session = await CreateSessionAsync(request, CancellationToken.None).ConfigureAwait(false);
        session.RunTask = Task.Run(
            async () =>
            {
                try
                {
                    await ExecutePipelineAsync(session, request, session.Cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (session.Item.Status != WorkItemStatuses.Aborted)
                    {
                        session.ErrorMessage = "Aborted.";
                        session.Item.Status = WorkItemStatuses.Aborted;
                        session.Item.UpdatedAt = DateTimeOffset.UtcNow;
                    }

                    Bump(session);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Interactive pipeline failed for {WorkItemId}", session.Item.WorkItemId);
                    session.ErrorMessage = ex.Message;
                    session.Item.Status = WorkItemStatuses.Failed;
                    session.Item.UpdatedAt = DateTimeOffset.UtcNow;
                    try
                    {
                        await AppendEventAsync(
                                session,
                                session.Item.CurrentStage,
                                WorkItemStatuses.Failed,
                                "PipelineFailed",
                                ex.Message,
                                session.Item.CurrentTaskId,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }

                    Bump(session);
                }
            },
            CancellationToken.None);

        return new PocPipelineStartResult(
            session.Item.ProjectId,
            session.Item.WorkItemId,
            session.Item.Status,
            session.Item.CurrentStage,
            "Pipeline started. Waiting for stage progress and human approvals at PRD, System Design, and Deploy.");
    }

    public async Task ApproveAsync(string workItemId, string? feedback, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(workItemId, out var session))
        {
            throw new InvalidOperationException($"Unknown work item {workItemId}. Start a pipeline first.");
        }

        if (string.IsNullOrWhiteSpace(session.PendingGate))
        {
            throw new InvalidOperationException("No gate is waiting for approval.");
        }

        session.ApprovalFeedback = feedback;
        if (session.ApprovalGate is null || session.ApprovalGate.Task.IsCompleted)
        {
            throw new InvalidOperationException("Approval gate is not active.");
        }

        session.ApprovalGate.TrySetResult(true);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<PocSteerResult> SteerAsync(
        string workItemId,
        string command,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession(workItemId);
        var cmd = command.Trim().ToUpperInvariant();
        return cmd switch
        {
            "PAUSE" => await RequestPauseAsync(session, cancellationToken).ConfigureAwait(false),
            "STOP" => await RequestStopAsync(session, cancellationToken).ConfigureAwait(false),
            "ABORT" => await RequestAbortAsync(session, cancellationToken).ConfigureAwait(false),
            "CONTINUE" => await RequestContinueAsync(session, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Command must be PAUSE, STOP, ABORT, or CONTINUE.")
        };
    }

    public PocPipelineStatusDto GetStatus(string workItemId)
    {
        var session = RequireSession(workItemId);
        return BuildStatus(session);
    }

    public IReadOnlyList<PocPipelineEventDto> GetEvents(string workItemId, int? afterSequence = null)
    {
        var session = RequireSession(workItemId);
        lock (_eventLock)
        {
            return session.Events
                .Where(e => afterSequence is null || e.Sequence > afterSequence.Value)
                .Select(ToEventDto)
                .ToArray();
        }
    }

    public IReadOnlyList<PocArtifactListItem> GetArtifacts(string workItemId)
    {
        var session = RequireSession(workItemId);
        return session.ArtifactIndex
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => new PocArtifactListItem(a.Key, a.Revision, a.BlobPath, TitleFor(a.Key)))
            .ToArray();
    }

    public async Task<PocArtifactContentDto?> GetArtifactContentAsync(
        string workItemId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession(workItemId);
        var entry = session.ArtifactIndex.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var content = await ReadArtifactBodyAsync(entry.BlobPath, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return null;
        }

        return new PocArtifactContentDto(entry.Key, entry.BlobPath, "application/json", content);
    }

    public IReadOnlyList<PocLogEntryDto> GetLogs(string workItemId)
    {
        var session = RequireSession(workItemId);
        lock (_eventLock)
        {
            return session.Events
                .Select(e => new PocLogEntryDto(
                    e.Entity.OccurredAt,
                    e.Entity.Status is WorkItemStatuses.Failed ? "error" : "info",
                    e.Entity.Stage,
                    e.Entity.EventType,
                    FormatLogLine(e.Entity)))
                .ToArray();
        }
    }

    public ChannelReader<PocPipelineStatusDto> Subscribe(string workItemId)
    {
        var session = RequireSession(workItemId);
        var channel = Channel.CreateUnbounded<PocPipelineStatusDto>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        session.Subscribers.Add(channel.Writer);
        channel.Writer.TryWrite(BuildStatus(session));
        return channel.Reader;
    }

    private PipelineSession RequireSession(string workItemId) =>
        _sessions.TryGetValue(workItemId, out var session)
            ? session
            : throw new InvalidOperationException($"Unknown work item {workItemId}.");

    private async Task<PocSteerResult> RequestPauseAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        if (WorkItemStatuses.IsTerminal(session.Item.Status))
        {
            throw new InvalidOperationException("Pipeline is already finished.");
        }

        if (session.Item.Status == WorkItemStatuses.Paused)
        {
            return new PocSteerResult(session.Item.WorkItemId, "PAUSE", session.Item.Status, "Already paused.");
        }

        session.PauseRequested = true;
        session.SteerHint = session.Item.Status == WorkItemStatuses.AwaitingApproval
            ? "PAUSE noted. Approve to pass this gate (then the run will pause), or STOP/ABORT now."
            : "PAUSE requested — will pause after the current stage finishes. Then type CONTINUE, STOP, or ABORT.";
        await AppendEventAsync(
                session,
                session.Item.CurrentStage,
                session.Item.Status,
                "SteerPauseRequested",
                session.SteerHint,
                session.Item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);
        Bump(session);

        return new PocSteerResult(
            session.Item.WorkItemId,
            "PAUSE",
            session.Item.Status,
            session.SteerHint ?? "PAUSE accepted.");
    }

    private async Task<PocSteerResult> RequestStopAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        if (WorkItemStatuses.IsTerminal(session.Item.Status))
        {
            throw new InvalidOperationException("Pipeline is already finished.");
        }

        session.StopRequested = true;
        session.PauseRequested = false;
        var last = session.LastApprovedStage ?? "INTAKE";
        session.SteerHint =
            $"STOP requested — after the current stage (if running), revert to last approved ({last}) and wait for you.";
        await AppendEventAsync(
                session,
                session.Item.CurrentStage,
                session.Item.Status,
                "SteerStopRequested",
                session.SteerHint,
                session.Item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);

        if (session.ApprovalGate is { Task.IsCompleted: false })
        {
            session.PendingGate = null;
            session.PendingGateLabel = null;
            session.ApprovalGate.TrySetResult(false);
        }
        else if (session.Item.Status is WorkItemStatuses.Queued or WorkItemStatuses.Paused
                 or WorkItemStatuses.HumanActionRequired or WorkItemStatuses.AwaitingApproval)
        {
            await ApplyStopAndPauseAsync(session, cancellationToken).ConfigureAwait(false);
        }

        Bump(session);
        return new PocSteerResult(session.Item.WorkItemId, "STOP", session.Item.Status, session.SteerHint);
    }

    private async Task<PocSteerResult> RequestAbortAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        session.AbortRequested = true;
        session.PauseRequested = false;
        session.StopRequested = false;
        session.SteerHint = "ABORT — discarding this work item and returning to idle chat.";
        session.Item.Status = WorkItemStatuses.Aborted;
        session.Item.UpdatedAt = DateTimeOffset.UtcNow;
        session.PendingGate = null;
        session.PendingGateLabel = null;
        session.ErrorMessage = "Aborted by user.";
        await UpsertWorkItemAsync(session.Item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(
                session,
                session.Item.CurrentStage,
                WorkItemStatuses.Aborted,
                "SteerAborted",
                session.SteerHint,
                session.Item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);

        session.ApprovalGate?.TrySetResult(false);
        session.ContinueGate?.TrySetResult(true);
        try
        {
            await session.Cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        session.Audit.CompletedAt = DateTimeOffset.UtcNow;
        session.Audit.FinalStatus = WorkItemStatuses.Aborted;
        await WriteAuditAsync(session, cancellationToken).ConfigureAwait(false);
        Bump(session);
        return new PocSteerResult(session.Item.WorkItemId, "ABORT", WorkItemStatuses.Aborted, session.SteerHint);
    }

    private async Task<PocSteerResult> RequestContinueAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        if (session.Item.Status != WorkItemStatuses.Paused
            && session.ContinueGate is null)
        {
            throw new InvalidOperationException("Nothing is paused — CONTINUE is only valid after PAUSE or STOP.");
        }

        // Capture before releasing ContinueGate — WaitWhilePausedAsync clears SteerHint on the pipeline thread.
        const string message = "CONTINUE — resuming the pipeline.";
        session.SteerHint = message;
        await AppendEventAsync(
                session,
                session.Item.CurrentStage,
                "Resuming",
                "SteerContinue",
                message,
                session.Item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);

        if (session.ContinueGate is { Task.IsCompleted: false })
        {
            session.ContinueGate.TrySetResult(true);
        }

        // If we paused while a PRD/SD/Deploy gate was still pending, restore AwaitingApproval.
        if (!string.IsNullOrWhiteSpace(session.SuspendedGate))
        {
            session.PendingGate = session.SuspendedGate;
            session.PendingGateLabel = session.SuspendedGateLabel;
            session.SuspendedGate = null;
            session.SuspendedGateLabel = null;
            session.Item.Status = WorkItemStatuses.AwaitingApproval;
            session.Item.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertWorkItemAsync(session.Item, cancellationToken).ConfigureAwait(false);
        }
        else if (session.Item.Status == WorkItemStatuses.Paused)
        {
            session.Item.Status = WorkItemStatuses.Queued;
            session.Item.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertWorkItemAsync(session.Item, cancellationToken).ConfigureAwait(false);
        }

        Bump(session);
        return new PocSteerResult(session.Item.WorkItemId, "CONTINUE", session.Item.Status, message);
    }

    private async Task EnterPausedAsync(PipelineSession session, string message, CancellationToken cancellationToken)
    {
        if (session.Item.Status == WorkItemStatuses.AwaitingApproval && session.PendingGate is not null)
        {
            session.SuspendedGate = session.PendingGate;
            session.SuspendedGateLabel = session.PendingGateLabel;
            session.PendingGate = null;
            session.PendingGateLabel = null;
        }

        session.Item.Status = WorkItemStatuses.Paused;
        session.Item.UpdatedAt = DateTimeOffset.UtcNow;
        session.SteerHint = message;
        session.ContinueGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await UpsertWorkItemAsync(session.Item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(
                session,
                session.Item.CurrentStage,
                WorkItemStatuses.Paused,
                "SteerPaused",
                message,
                session.Item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);
        Bump(session);
    }

    private async Task ApplyStopAndPauseAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        var last = session.LastApprovedStage ?? "INTAKE";
        session.Item.CurrentStage = last;
        session.PendingGate = null;
        session.PendingGateLabel = null;
        session.SuspendedGate = null;
        session.SuspendedGateLabel = null;
        session.ResumeFromStage = NextStageAfter(last);
        session.StopRequested = false;
        await EnterPausedAsync(
                session,
                $"Stopped. Reverted to last approved stage ({last}). "
                + $"Type CONTINUE to resume from {session.ResumeFromStage ?? "the next stage"}, or ABORT to discard the work item.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WaitWhilePausedAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        if (session.ContinueGate is null)
        {
            return;
        }

        await session.ContinueGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        session.ContinueGate = null;
        session.SteerHint = null;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task HonorSteerAfterStageAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (session.StopRequested)
        {
            await ApplyStopAndPauseAsync(session, cancellationToken).ConfigureAwait(false);
            await WaitWhilePausedAsync(session, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (session.PauseRequested)
        {
            session.PauseRequested = false;
            await EnterPausedAsync(
                    session,
                    "Paused after the current stage. Type CONTINUE to proceed, STOP to revert to last approved, or ABORT.",
                    cancellationToken)
                .ConfigureAwait(false);
            await WaitWhilePausedAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? NextStageAfter(string stage)
    {
        if (stage is "INTAKE" or "")
        {
            return "PRD";
        }

        var idx = Array.IndexOf(StageOrder, stage);
        if (idx < 0)
        {
            return "PRD";
        }

        return idx + 1 < StageOrder.Length ? StageOrder[idx + 1] : null;
    }

    private async Task<PipelineSession> CreateSessionAsync(
        PocPipelineStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            throw new ArgumentException("UserRequest is required.", nameof(request));
        }

        var stack = string.IsNullOrWhiteSpace(request.StackProfile)
            ? InferStack(request.UserRequest)
            : request.StackProfile.Trim();
        var mode = string.IsNullOrWhiteSpace(request.ProjectMode) ? "New" : request.ProjectMode.Trim();
        var projectName = string.IsNullOrWhiteSpace(request.ProjectName)
            ? (stack == "fe-be-llm" ? "Hello World" : "Calculator")
            : request.ProjectName.Trim();
        var projectSlug = Slug(projectName);

        var projectId = request.ProjectId?.Trim();
        if (string.IsNullOrWhiteSpace(projectId))
        {
            projectId = Guid.CreateVersion7().ToString("N");
            if (!_options.UseInMemoryTables)
            {
                await _projects
                    .UpsertAsync(
                        new ProjectEntity
                        {
                            PartitionKey = ProjectEntity.DefaultPartitionKey,
                            RowKey = projectId,
                            Name = projectName,
                            CreatedBy = _options.DummyUserId,
                            CreatedAt = DateTimeOffset.UtcNow
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        WorkItemEntity item;
        if (_options.UseInMemoryTables)
        {
            item = new WorkItemEntity
            {
                PartitionKey = projectId,
                RowKey = Guid.CreateVersion7().ToString("N"),
                WorkItemId = Guid.CreateVersion7().ToString("N"),
                ProjectId = projectId,
                ProjectName = projectName,
                UserId = _options.DummyUserId,
                Title = projectName,
                Status = WorkItemStatuses.Draft,
                CurrentStage = string.Empty,
                CurrentInputVersion = 0,
                PipelineVersion = 1,
                StackProfile = stack,
                TemplateId = $"{stack}@1.0.0",
                UserRequest = request.UserRequest,
                BlobPrefix = $"{projectId}/pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            item.RowKey = item.WorkItemId;
            item.BlobPrefix = $"{projectId}/{item.WorkItemId}/v1/";
            _memoryItems[item.WorkItemId] = item;
        }
        else
        {
            var created = await _workItems
                .TryCreateAsync(projectId, projectName, _options.DummyUserId, null, projectName, cancellationToken)
                .ConfigureAwait(false);
            item = created.Item ?? throw new InvalidOperationException("Failed to create work item.");
            item.StackProfile = stack;
            item.TemplateId = $"{stack}@1.0.0";
            item.UserRequest = request.UserRequest;
            item.BlobPrefix = $"{projectId}/{item.WorkItemId}/v1/";
            item.Status = WorkItemStatuses.Draft;
            await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        }

        var session = new PipelineSession
        {
            Item = item,
            ProjectSlug = projectSlug,
            ProjectMode = mode,
            AutoApprove = request.AutoApprove,
            BaselineArtifacts = request.BaselineArtifacts,
            Audit = new AuditLogDocument
            {
                OriginalRequest = request.UserRequest,
                ProjectId = projectId,
                WorkItemId = item.WorkItemId,
                StackProfile = stack,
                ProjectMode = mode,
                StartedAt = DateTimeOffset.UtcNow
            },
            Cts = new CancellationTokenSource()
        };
        _sessions[item.WorkItemId] = session;
        return session;
    }

    private async Task ExecutePipelineAsync(
        PipelineSession session,
        PocPipelineStartRequest request,
        CancellationToken cancellationToken)
    {
        var item = session.Item;
        var audit = session.Audit;
        var stack = string.IsNullOrWhiteSpace(item.StackProfile) ? "fe-be" : item.StackProfile;
        var mode = session.ProjectMode;
        var projectName = item.ProjectName ?? "Project";
        var projectSlug = session.ProjectSlug;

        await AppendEventAsync(session, "INTAKE", WorkItemStatuses.Draft, "IntakeStarted", "Intake one-shot", null, cancellationToken)
            .ConfigureAwait(false);

        var intakePath = $"{item.BlobPrefix}intake-summary.json";
        var intakeJson = JsonSerializer.Serialize(
            new
            {
                request = request.UserRequest,
                stackProfile = stack,
                projectMode = mode,
                projectName,
                projectSlug,
                templateId = item.TemplateId
            },
            JsonOptions);
        await WriteLocalArtifactAsync(intakePath, intakeJson, cancellationToken).ConfigureAwait(false);
        await RegisterArtifactAsync(session, "intakeSummary", 1, intakePath, cancellationToken).ConfigureAwait(false);
        audit.Transitions.Add(new AuditTransition
        {
            Stage = "INTAKE",
            Status = WorkItemStatuses.Draft,
            At = DateTimeOffset.UtcNow,
            Message = "Wrote intake-summary.json",
            ArtifactPath = intakePath
        });
        await WriteAuditAsync(session, cancellationToken).ConfigureAwait(false);

        item.Status = WorkItemStatuses.Queued;
        item.CurrentStage = "PRD";
        item.CurrentInputVersion = 1;
        item.PipelineStarted = true;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(session, "PRD", WorkItemStatuses.Queued, "PipelineStarted", "Pipeline started", null, cancellationToken)
            .ConfigureAwait(false);
        session.LastApprovedStage = "INTAKE";

        string? workspaceRoot = null;
        string? headCommit = null;
        string? deployedUrl = null;
        var artifacts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["intakeSummary"] = intakePath
        };

        if (mode.Equals("Existing", StringComparison.OrdinalIgnoreCase)
            && request.BaselineArtifacts is { Count: > 0 })
        {
            foreach (var pair in request.BaselineArtifacts)
            {
                artifacts[pair.Key] = pair.Value;
            }
        }

        for (var stageIndex = 0; stageIndex < StageOrder.Length; stageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.AbortRequested)
            {
                throw new OperationCanceledException("Aborted by user.");
            }

            // After STOP + CONTINUE, jump to the stage after last approved.
            if (session.ResumeFromStage is { } resume)
            {
                var jump = Array.IndexOf(StageOrder, resume);
                session.ResumeFromStage = null;
                if (jump >= 0 && jump != stageIndex)
                {
                    stageIndex = jump;
                }
            }

            var stage = StageOrder[stageIndex];

            if (stage == "DEPLOY_DEV")
            {
                var deployResult = await RunStageAsync(
                        session,
                        stage,
                        "Deploy",
                        projectName,
                        projectSlug,
                        stack,
                        mode,
                        artifacts,
                        workspaceRoot,
                        headCommit,
                        request.BaselineArtifacts,
                        cancellationToken)
                    .ConfigureAwait(false);
                MergeArtifacts(artifacts, deployResult.Artifacts);
                workspaceRoot = deployResult.Outputs.GetValueOrDefault("workspaceRoot") ?? workspaceRoot;
                headCommit = deployResult.Outputs.GetValueOrDefault("headCommit") ?? headCommit;
                deployedUrl = deployResult.Outputs.GetValueOrDefault("deployedUrls");
                session.DeployedUrl = deployedUrl;
                item.RepoFactsJson = JsonSerializer.Serialize(
                    new { headCommit, deployedUrl, workspaceRoot },
                    JsonOptions);

                var deployApproved = await RaiseGateAndWaitAsync(
                        session,
                        stage,
                        "DEV acceptance — open the app, then approve to finalize",
                        deployResult.TaskId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!deployApproved)
                {
                    await HonorSteerAfterStageAsync(session, cancellationToken).ConfigureAwait(false);
                    stageIndex = -1; // loop + ResumeFromStage will retarget
                    continue;
                }

                await HonorSteerAfterStageAsync(session, cancellationToken).ConfigureAwait(false);
                if (session.ResumeFromStage is not null)
                {
                    stageIndex = -1;
                    continue;
                }

                var finalizeResult = await RunStageAsync(
                        session,
                        stage,
                        "Finalize",
                        projectName,
                        projectSlug,
                        stack,
                        mode,
                        artifacts,
                        workspaceRoot,
                        headCommit,
                        request.BaselineArtifacts,
                        cancellationToken)
                    .ConfigureAwait(false);
                MergeArtifacts(artifacts, finalizeResult.Artifacts);
                var mergeCommit = finalizeResult.Outputs.GetValueOrDefault("mergeCommit") ?? headCommit;
                item.Status = WorkItemStatuses.Completed;
                item.CurrentStage = "DONE";
                item.RepoFactsJson = JsonSerializer.Serialize(
                    new { headCommit, deployedUrl, workspaceRoot, mergeCommit },
                    JsonOptions);
                item.UpdatedAt = DateTimeOffset.UtcNow;
                item.LastAppliedResultId = $"result:{finalizeResult.TaskId}:{item.CurrentInputVersion}";
                await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
                await AppendEventAsync(
                        session,
                        "DONE",
                        WorkItemStatuses.Completed,
                        "Finalized",
                        $"Completed with URL {deployedUrl}",
                        finalizeResult.TaskId,
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteBaselineAsync(item, artifacts, mergeCommit, cancellationToken).ConfigureAwait(false);
                break;
            }

            var result = await RunStageAsync(
                    session,
                    stage,
                    deploymentAction: null,
                    projectName,
                    projectSlug,
                    stack,
                    mode,
                    artifacts,
                    workspaceRoot,
                    headCommit,
                    request.BaselineArtifacts,
                    cancellationToken)
                .ConfigureAwait(false);
            MergeArtifacts(artifacts, result.Artifacts);
            workspaceRoot = result.Outputs.GetValueOrDefault("workspaceRoot") ?? workspaceRoot;
            headCommit = result.Outputs.GetValueOrDefault("headCommit") ?? headCommit;

            if (stage is "PRD" or "SYSTEM_DESIGN")
            {
                var label = stage == "PRD"
                    ? "Review the PRD document, then approve to continue"
                    : "Review the System Design document, then approve to continue";
                var approved = await RaiseGateAndWaitAsync(session, stage, label, result.TaskId, cancellationToken)
                    .ConfigureAwait(false);
                if (!approved)
                {
                    await HonorSteerAfterStageAsync(session, cancellationToken).ConfigureAwait(false);
                    stageIndex = -1;
                    continue;
                }
            }
            else
            {
                item.Status = WorkItemStatuses.Queued;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
                Bump(session);
            }

            await HonorSteerAfterStageAsync(session, cancellationToken).ConfigureAwait(false);
            if (session.ResumeFromStage is not null)
            {
                stageIndex = -1;
            }
        }

        if (session.AbortRequested || item.Status == WorkItemStatuses.Aborted)
        {
            return;
        }

        audit.CompletedAt = DateTimeOffset.UtcNow;
        audit.FinalStatus = WorkItemStatuses.Completed;
        audit.DeployedUrl = deployedUrl;
        session.DeployedUrl = deployedUrl;
        await WriteAuditAsync(session, cancellationToken).ConfigureAwait(false);
        Bump(session);
    }

    /// <returns>true if approved and pipeline should continue; false if STOP aborted the gate.</returns>
    private async Task<bool> RaiseGateAndWaitAsync(
        PipelineSession session,
        string stage,
        string label,
        string? taskId,
        CancellationToken cancellationToken)
    {
        var item = session.Item;
        item.Status = WorkItemStatuses.AwaitingApproval;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        session.PendingGate = stage;
        session.PendingGateLabel = label;
        await AppendEventAsync(
                session,
                stage,
                WorkItemStatuses.AwaitingApproval,
                "GateRaised",
                label,
                taskId,
                cancellationToken)
            .ConfigureAwait(false);

        if (session.AutoApprove)
        {
            await RecordApprovalAsync(session, stage, auto: true, cancellationToken).ConfigureAwait(false);
            return true;
        }

        session.ApprovalGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bump(session);
        var waitMessage = stage switch
        {
            "PRD" => "PRD is ready for review. Open the document, then approve to continue.",
            "SYSTEM_DESIGN" => "System Design is ready for review. Open the document, then approve to continue.",
            "DEPLOY_DEV" => "DEV deploy is ready. Open the app URL, then approve to finalize.",
            _ => "Waiting for your approval in the chat…"
        };
        await AppendEventAsync(
                session,
                stage,
                WorkItemStatuses.AwaitingApproval,
                "WaitingForUser",
                waitMessage,
                taskId,
                cancellationToken)
            .ConfigureAwait(false);

        var approved = await session.ApprovalGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        session.ApprovalGate = null;
        if (!approved)
        {
            if (session.AbortRequested)
            {
                throw new OperationCanceledException("Aborted by user.");
            }

            // STOP while at gate — do not record approval; caller will pause/rewind.
            session.StopRequested = true;
            return false;
        }

        await RecordApprovalAsync(session, stage, auto: false, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RecordApprovalAsync(
        PipelineSession session,
        string stage,
        bool auto,
        CancellationToken cancellationToken)
    {
        var item = session.Item;
        var approval = new ApprovalEntity
        {
            PartitionKey = item.WorkItemId,
            RowKey = $"{stage}:{item.CurrentInputVersion}",
            WorkItemId = item.WorkItemId,
            Stage = stage,
            Decision = "Approved",
            UserId = _options.DummyUserId,
            InputVersion = item.CurrentInputVersion,
            DecidedAt = DateTimeOffset.UtcNow
        };
        _memoryApprovals.Add(approval);
        if (!_options.UseInMemoryTables)
        {
            await _tables.GetClient(TableNames.Approvals)
                .UpsertEntityAsync(approval, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        item.CurrentInputVersion += 1;
        item.Status = WorkItemStatuses.Queued;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        session.PendingGate = null;
        session.PendingGateLabel = null;
        session.ApprovalGate = null;
        session.LastApprovedStage = stage;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);

        var message = auto
            ? $"Auto-approved by {_options.DummyUserId}"
            : $"Approved by {_options.DummyUserId}"
              + (string.IsNullOrWhiteSpace(session.ApprovalFeedback) ? string.Empty : $": {session.ApprovalFeedback}");
        await AppendEventAsync(
                session,
                stage,
                "Approved",
                "GateApproved",
                message,
                item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);
        session.Audit.Transitions.Add(new AuditTransition
        {
            Stage = stage,
            Status = "Approved",
            At = DateTimeOffset.UtcNow,
            Message = $"causationId=gate:{approval.RowKey}:Approved"
        });
        await WriteAuditAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StageDispatchResult> RunStageAsync(
        PipelineSession session,
        string stage,
        string? deploymentAction,
        string projectName,
        string projectSlug,
        string stack,
        string mode,
        Dictionary<string, string> artifacts,
        string? workspaceRoot,
        string? headCommit,
        Dictionary<string, string>? baseline,
        CancellationToken cancellationToken)
    {
        var item = session.Item;
        item.CurrentInputVersion = Math.Max(1, item.CurrentInputVersion);

        var taskId = Guid.CreateVersion7().ToString("N");
        item.CurrentTaskId = taskId;
        item.CurrentStage = stage;
        item.Status = WorkItemStatuses.Running;
        item.PendingMessage = $"{stage}:{taskId}:{item.CurrentInputVersion}";
        item.PendingAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        var actionLabel = deploymentAction is null ? stage : $"{stage}:{deploymentAction}";
        await AppendEventAsync(
                session,
                stage,
                WorkItemStatuses.Running,
                "StageStarted",
                $"Running {actionLabel}…",
                taskId,
                cancellationToken)
            .ConfigureAwait(false);

        var task = new
        {
            workItemId = item.WorkItemId,
            projectId = item.ProjectId,
            stage,
            taskId,
            correlationId = Guid.NewGuid().ToString("N"),
            causationId = item.PendingMessage,
            inputVersion = item.CurrentInputVersion,
            outputRevision = 1,
            pipelineVersion = item.PipelineVersion,
            blobPrefix = item.BlobPrefix,
            stateContextId = $"{item.ProjectId}:{item.WorkItemId}",
            artifacts,
            context = new
            {
                userRequest = item.UserRequest,
                projectName,
                projectSlug,
                projectMode = mode,
                stackProfile = stack,
                templateId = item.TemplateId,
                environment = "DEV",
                deploymentAction,
                workBranch = $"feature/wi-{item.WorkItemId}",
                headCommit,
                workspaceRoot,
                baselineArtifacts = baseline ?? new Dictionary<string, string>()
            },
            config = new
            {
                modelId = string.IsNullOrWhiteSpace(_options.ModelId) ? null : _options.ModelId,
                reasoningEffort = "low"
            }
        };

        var client = _httpClientFactory.CreateClient("agenthub");
        using var response = await client
            .PostAsJsonAsync("/api/pipeline/dispatch", task, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"AgentHub dispatch failed ({(int)response.StatusCode}): {body}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<AgentHubResultDto>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty AgentResult from AgentHub.");

        if (!string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Stage {stage} failed: {result.ErrorMessage}");
        }

        foreach (var pair in result.Artifacts ?? [])
        {
            await RegisterArtifactAsync(session, pair.Key, 1, pair.Value, cancellationToken).ConfigureAwait(false);
        }

        item.LastAppliedResultId = $"result:{taskId}:{item.CurrentInputVersion}";
        item.PendingMessage = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(
                session,
                stage,
                "Completed",
                "StageCompleted",
                result.Outputs?.GetValueOrDefault("deployedUrls") ?? $"{actionLabel} completed",
                taskId,
                cancellationToken)
            .ConfigureAwait(false);

        session.Audit.Transitions.Add(new AuditTransition
        {
            Stage = stage + (deploymentAction is null ? string.Empty : ":" + deploymentAction),
            Status = "Completed",
            At = DateTimeOffset.UtcNow,
            Message = $"task {taskId}",
            ArtifactPath = result.Artifacts?.Values.FirstOrDefault()
        });
        await WriteAuditAsync(session, cancellationToken).ConfigureAwait(false);

        return new StageDispatchResult(
            taskId,
            result.Artifacts ?? new Dictionary<string, string>(),
            result.Outputs ?? new Dictionary<string, string>());
    }

    private async Task AppendEventAsync(
        PipelineSession session,
        string stage,
        string status,
        string eventType,
        string message,
        string? taskId,
        CancellationToken cancellationToken)
    {
        var item = session.Item;
        var entity = new WorkItemEventEntity
        {
            PartitionKey = item.WorkItemId,
            RowKey = Guid.CreateVersion7().ToString("N"),
            WorkItemId = item.WorkItemId,
            ProjectId = item.ProjectId,
            Stage = stage,
            Status = status,
            EventType = eventType,
            Message = message,
            TaskId = taskId,
            OccurredAt = DateTimeOffset.UtcNow
        };
        lock (_eventLock)
        {
            _memoryEvents.Add(entity);
            session.Events.Add(new SessionEvent(++session.Sequence, entity, GuessArtifactKey(eventType, stage, session)));
        }

        _logger.LogInformation(
            "WorkItemEvent {EventType} WI={WorkItemId} Stage={Stage} Status={Status}",
            eventType,
            item.WorkItemId,
            stage,
            status);
        if (!_options.UseInMemoryTables)
        {
            await _tables.GetClient(TableNames.WorkItemEvents)
                .UpsertEntityAsync(entity, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        Bump(session);
    }

    private async Task RegisterArtifactAsync(
        PipelineSession session,
        string key,
        int revision,
        string path,
        CancellationToken cancellationToken)
    {
        var item = session.Item;
        var row = new ArtifactEntity
        {
            PartitionKey = item.WorkItemId,
            RowKey = $"{key}:{revision}",
            WorkItemId = item.WorkItemId,
            RegistryKey = key,
            Revision = revision,
            BlobPath = path
        };
        var head = new ArtifactEntity
        {
            PartitionKey = item.WorkItemId,
            RowKey = $"~HEAD_{key}",
            WorkItemId = item.WorkItemId,
            RegistryKey = key,
            Revision = revision,
            BlobPath = path
        };
        _memoryArtifacts[row.RowKey] = row;
        _memoryArtifacts[head.RowKey] = head;
        session.ArtifactIndex.RemoveAll(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        session.ArtifactIndex.Add(new ArtifactIndexEntry(key, revision, path));
        if (!_options.UseInMemoryTables)
        {
            var client = _tables.GetClient(TableNames.Artifacts);
            await client.UpsertEntityAsync(row, cancellationToken: cancellationToken).ConfigureAwait(false);
            await client.UpsertEntityAsync(head, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Surface human-facing docs in chat; skip noisy intermediate keys.
        if (key is "prd" or "prdMd" or "techSpec" or "techSpecMd" or "completionSummary")
        {
            await AppendEventAsync(
                    session,
                    item.CurrentStage,
                    "ArtifactReady",
                    "ArtifactReady",
                    $"Document ready: {TitleFor(key)}",
                    item.CurrentTaskId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task UpsertWorkItemAsync(WorkItemEntity item, CancellationToken cancellationToken)
    {
        _memoryItems[item.WorkItemId] = item;
        if (_options.UseInMemoryTables)
        {
            return;
        }

        await _tables.GetClient(TableNames.WorkItems)
            .UpsertEntityAsync(item, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteBaselineAsync(
        WorkItemEntity item,
        Dictionary<string, string> artifacts,
        string? mergeCommit,
        CancellationToken cancellationToken)
    {
        var baseline = new WorkItemBaselineEntity
        {
            PartitionKey = item.ProjectId,
            RowKey = WorkItemBaselineEntity.BaselineRowKey,
            WorkItemId = item.WorkItemId,
            Generation = 1,
            PrdPath = artifacts.GetValueOrDefault("prd"),
            TechSpecPath = artifacts.GetValueOrDefault("techSpec"),
            CodeStatePath = artifacts.GetValueOrDefault("codeState"),
            MergeCommit = mergeCommit,
            TemplateId = item.TemplateId,
            StackProfile = item.StackProfile,
            AcceptedAt = DateTimeOffset.UtcNow
        };
        if (_options.UseInMemoryTables)
        {
            return;
        }

        await _tables.GetClient(TableNames.WorkItems)
            .UpsertEntityAsync(baseline, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _tables.GetClient(TableNames.WorkItems)
                .DeleteEntityAsync(item.ProjectId, WorkItemEntity.ActiveRowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // optional
        }
    }

    private async Task WriteAuditAsync(PipelineSession session, CancellationToken cancellationToken)
    {
        var item = session.Item;
        var audit = session.Audit;
        Directory.CreateDirectory(_options.AuditLogDirectory);
        var path = AuditLogPath(item);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(audit, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        var blobStyle = Path.Combine(
            _options.AuditLogDirectory,
            item.ProjectId,
            item.WorkItemId,
            "v1",
            "AuditLog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(blobStyle)!);
        await File.WriteAllTextAsync(blobStyle, json, cancellationToken).ConfigureAwait(false);
        await WriteLocalArtifactAsync($"{item.BlobPrefix}AuditLog.json", json, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteLocalArtifactAsync(string blobPath, string json, CancellationToken cancellationToken)
    {
        var full = Path.Combine(
            _options.ArtifactFileRootPath,
            blobPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, json, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ReadArtifactBodyAsync(string blobPath, CancellationToken cancellationToken)
    {
        var local = Path.Combine(
            _options.ArtifactFileRootPath,
            blobPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(local))
        {
            return await File.ReadAllTextAsync(local, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("agenthub");
            using var response = await client
                .GetAsync($"/api/pipeline/blob?path={Uri.EscapeDataString(blobPath)}", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read artifact {BlobPath}", blobPath);
            return null;
        }
    }

    private string AuditLogPath(WorkItemEntity item) =>
        Path.Combine(_options.AuditLogDirectory, $"{item.WorkItemId}-AuditLog.json");

    private PocPipelineStatusDto BuildStatus(PipelineSession session)
    {
        var item = session.Item;
        var current = item.CurrentStage;
        var terminal = WorkItemStatuses.IsTerminal(item.Status);
        var failed = item.Status is WorkItemStatuses.Failed or WorkItemStatuses.Aborted;
        var paused = item.Status == WorkItemStatuses.Paused;
        var stages = StageStrip.Select(s =>
        {
            var state = "pending";
            if (failed && s.Id == current)
            {
                state = "failed";
            }
            else if (item.Status == WorkItemStatuses.Completed || IsStageDone(session, s.Id, current, terminal))
            {
                state = "done";
            }
            else if (session.PendingGate == s.Id || session.SuspendedGate == s.Id)
            {
                state = "awaiting";
            }
            else if (paused && s.Id == current)
            {
                state = "awaiting";
            }
            else if (s.Id == current && item.Status == WorkItemStatuses.Running)
            {
                state = "running";
            }
            else if (s.Id == current)
            {
                state = "active";
            }

            return new PocStageStripItem(s.Id, s.Label, state);
        }).ToArray();

        return new PocPipelineStatusDto(
            item.ProjectId,
            item.WorkItemId,
            item.Status,
            item.CurrentStage,
            session.PendingGate ?? session.SuspendedGate,
            session.PendingGateLabel ?? session.SuspendedGateLabel,
            session.DeployedUrl,
            item.UserRequest ?? string.Empty,
            item.StackProfile ?? string.Empty,
            item.ProjectName ?? string.Empty,
            terminal,
            failed,
            session.ErrorMessage,
            stages,
            item.UpdatedAt,
            IsPaused: paused,
            CanResume: paused && session.ContinueGate is { Task.IsCompleted: false },
            LastApprovedStage: session.LastApprovedStage,
            SteerHint: session.SteerHint);
    }

    private static bool IsStageDone(PipelineSession session, string stageId, string current, bool terminal)
    {
        if (stageId == "DONE")
        {
            return terminal && session.Item.Status == WorkItemStatuses.Completed;
        }

        if (stageId == "INTAKE")
        {
            return session.Events.Any(e => e.Entity.EventType == "IntakeStarted");
        }

        var order = StageStrip.Select(s => s.Id).ToList();
        var stageIdx = order.IndexOf(stageId);
        var currentIdx = order.IndexOf(string.IsNullOrEmpty(current) ? "INTAKE" : current);
        if (stageIdx < 0 || currentIdx < 0)
        {
            return false;
        }

        if (stageIdx < currentIdx)
        {
            return true;
        }

        if (stageIdx == currentIdx
            && session.Events.Any(e =>
                e.Entity.Stage == stageId
                && e.Entity.EventType is "StageCompleted" or "GateApproved"))
        {
            return session.PendingGate != stageId;
        }

        return false;
    }

    private PocPipelineRunResult ToRunResult(PipelineSession session)
    {
        var item = session.Item;
        return new PocPipelineRunResult(
            item.ProjectId,
            item.WorkItemId,
            item.Status,
            item.CurrentStage,
            session.DeployedUrl,
            AuditLogPath(item),
            session.Audit.Transitions.Select(t => t.Stage + ":" + t.Status).ToArray(),
            session.Events.Select(e => e.Entity.EventType).ToArray());
    }

    private void Bump(PipelineSession session)
    {
        var status = BuildStatus(session);
        foreach (var writer in session.Subscribers.ToArray())
        {
            if (session.DeadWriters.Contains(writer))
            {
                continue;
            }

            if (!writer.TryWrite(status))
            {
                session.DeadWriters.Add(writer);
            }
        }
    }

    private static void MergeArtifacts(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static string InferStack(string request) =>
        request.Contains("thought", StringComparison.OrdinalIgnoreCase)
        || request.Contains("llm", StringComparison.OrdinalIgnoreCase)
        || request.Contains("ollama", StringComparison.OrdinalIgnoreCase)
            ? "fe-be-llm"
            : "fe-be";

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray())
            .Trim('-');

    private static string TitleFor(string key) => key switch
    {
        "intakeSummary" => "Intake summary",
        "prd" => "PRD (JSON)",
        "prdMd" => "PRD",
        "techSpec" => "System design (JSON)",
        "techSpecMd" => "System design",
        "codeState" => "Code state",
        "review" => "Code review",
        "testResults" => "Test results",
        "completionSummary" => "Completion summary",
        _ => key
    };

    private static string? GuessArtifactKey(string eventType, string stage, PipelineSession session)
    {
        if (eventType is not ("ArtifactReady" or "GateRaised" or "WaitingForUser"))
        {
            return null;
        }

        return stage switch
        {
            "PRD" => session.ArtifactIndex.LastOrDefault(a => a.Key is "prdMd" or "prd")?.Key,
            "SYSTEM_DESIGN" => session.ArtifactIndex.LastOrDefault(a => a.Key is "techSpecMd" or "techSpec")?.Key,
            "DEPLOY_DEV" => session.ArtifactIndex.LastOrDefault(a => a.Key == "completionSummary")?.Key,
            _ => session.ArtifactIndex.LastOrDefault()?.Key
        };
    }

    private static string FormatLogLine(WorkItemEventEntity e) =>
        $"[{e.OccurredAt:HH:mm:ss}] {e.Stage}/{e.EventType} ({e.Status}) — {e.Message}";

    private static PocPipelineEventDto ToEventDto(SessionEvent e) =>
        new(
            e.Entity.RowKey,
            e.Entity.Stage,
            e.Entity.Status,
            e.Entity.EventType,
            e.Entity.Message,
            e.Entity.TaskId,
            e.Entity.OccurredAt,
            e.ArtifactKey);

    private sealed class AgentHubResultDto
    {
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public Dictionary<string, string>? Artifacts { get; set; }
        public Dictionary<string, string>? Outputs { get; set; }
    }

    private sealed record StageDispatchResult(
        string TaskId,
        Dictionary<string, string> Artifacts,
        Dictionary<string, string> Outputs);

    private sealed class PipelineSession
    {
        public required WorkItemEntity Item { get; init; }
        public required string ProjectSlug { get; init; }
        public required string ProjectMode { get; init; }
        public required bool AutoApprove { get; init; }
        public Dictionary<string, string>? BaselineArtifacts { get; init; }
        public required AuditLogDocument Audit { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public Task? RunTask { get; set; }
        public string? PendingGate { get; set; }
        public string? PendingGateLabel { get; set; }
        public string? SuspendedGate { get; set; }
        public string? SuspendedGateLabel { get; set; }
        public TaskCompletionSource<bool>? ApprovalGate { get; set; }
        public TaskCompletionSource<bool>? ContinueGate { get; set; }
        public string? ApprovalFeedback { get; set; }
        public string? DeployedUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LastApprovedStage { get; set; }
        public string? ResumeFromStage { get; set; }
        public string? SteerHint { get; set; }
        public bool PauseRequested { get; set; }
        public bool StopRequested { get; set; }
        public bool AbortRequested { get; set; }
        public int Sequence { get; set; }
        public List<SessionEvent> Events { get; } = [];
        public List<ArtifactIndexEntry> ArtifactIndex { get; } = [];
        public ConcurrentBag<ChannelWriter<PocPipelineStatusDto>> Subscribers { get; } = [];
        public ConcurrentBag<ChannelWriter<PocPipelineStatusDto>> DeadWriters { get; } = [];
    }

    private sealed record SessionEvent(int Sequence, WorkItemEventEntity Entity, string? ArtifactKey);

    private sealed record ArtifactIndexEntry(string Key, int Revision, string BlobPath);
}
