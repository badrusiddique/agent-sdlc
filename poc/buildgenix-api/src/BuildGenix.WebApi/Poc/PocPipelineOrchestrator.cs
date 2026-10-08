using System.Net.Http.Json;
using System.Text.Json;
using BuildGenix.Data;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using BuildGenix.Data.Repositories;
using Microsoft.Extensions.Options;

namespace BuildGenix.WebApi.Poc;

/// <summary>
/// Pre-phase-0 happy-path orchestrator. Publishes one stage at a time to the Pipeline agent,
/// updates WorkItems / WorkItemEvents / Approvals / Artifacts, and rewrites AuditLog.json.
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

        var audit = new AuditLogDocument
        {
            OriginalRequest = request.UserRequest,
            ProjectId = projectId,
            WorkItemId = item.WorkItemId,
            StackProfile = stack,
            ProjectMode = mode,
            StartedAt = DateTimeOffset.UtcNow
        };

        await AppendEventAsync(item, "INTAKE", WorkItemStatuses.Draft, "IntakeStarted", "Intake one-shot", null, cancellationToken)
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
        await RegisterArtifactAsync(item, "intakeSummary", 1, intakePath, cancellationToken).ConfigureAwait(false);
        audit.Transitions.Add(new AuditTransition
        {
            Stage = "INTAKE",
            Status = WorkItemStatuses.Draft,
            At = DateTimeOffset.UtcNow,
            Message = "Wrote intake-summary.json",
            ArtifactPath = intakePath
        });
        await WriteAuditAsync(item, audit, cancellationToken).ConfigureAwait(false);

        // Take ~ACTIVE / start pipeline
        item.Status = WorkItemStatuses.Queued;
        item.CurrentStage = "PRD";
        item.CurrentInputVersion = 1;
        item.PipelineStarted = true;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(item, "PRD", WorkItemStatuses.Queued, "PipelineStarted", "Implement now (POC auto)", null, cancellationToken)
            .ConfigureAwait(false);

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

        foreach (var stage in StageOrder)
        {
            if (stage == "DEPLOY_DEV")
            {
                // Deploy
                var deployResult = await RunStageAsync(
                        item,
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
                        audit,
                        cancellationToken)
                    .ConfigureAwait(false);
                MergeArtifacts(artifacts, deployResult.Artifacts);
                workspaceRoot = deployResult.Outputs.GetValueOrDefault("workspaceRoot") ?? workspaceRoot;
                headCommit = deployResult.Outputs.GetValueOrDefault("headCommit") ?? headCommit;
                deployedUrl = deployResult.Outputs.GetValueOrDefault("deployedUrls");
                item.RepoFactsJson = JsonSerializer.Serialize(
                    new { headCommit, deployedUrl, workspaceRoot },
                    JsonOptions);

                item.Status = WorkItemStatuses.AwaitingApproval;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
                await AppendEventAsync(
                        item,
                        stage,
                        WorkItemStatuses.AwaitingApproval,
                        "GateRaised",
                        "DEV acceptance",
                        deployResult.TaskId,
                        cancellationToken)
                    .ConfigureAwait(false);
                await AutoApproveAsync(item, stage, audit, cancellationToken).ConfigureAwait(false);

                // Finalize
                var finalizeResult = await RunStageAsync(
                        item,
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
                        audit,
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
                        item,
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
                    item,
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
                    audit,
                    cancellationToken)
                .ConfigureAwait(false);
            MergeArtifacts(artifacts, result.Artifacts);
            workspaceRoot = result.Outputs.GetValueOrDefault("workspaceRoot") ?? workspaceRoot;
            headCommit = result.Outputs.GetValueOrDefault("headCommit") ?? headCommit;

            if (stage is "PRD" or "SYSTEM_DESIGN")
            {
                item.Status = WorkItemStatuses.AwaitingApproval;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
                await AppendEventAsync(
                        item,
                        stage,
                        WorkItemStatuses.AwaitingApproval,
                        "GateRaised",
                        $"{stage} review",
                        result.TaskId,
                        cancellationToken)
                    .ConfigureAwait(false);
                await AutoApproveAsync(item, stage, audit, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                item.Status = WorkItemStatuses.Queued;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }

        audit.CompletedAt = DateTimeOffset.UtcNow;
        audit.FinalStatus = WorkItemStatuses.Completed;
        audit.DeployedUrl = deployedUrl;
        await WriteAuditAsync(item, audit, cancellationToken).ConfigureAwait(false);

        return new PocPipelineRunResult(
            item.ProjectId,
            item.WorkItemId,
            item.Status,
            item.CurrentStage,
            deployedUrl,
            AuditLogPath(item),
            audit.Transitions.Select(t => t.Stage + ":" + t.Status).ToArray(),
            _memoryEvents.Where(e => e.WorkItemId == item.WorkItemId).Select(e => e.EventType).ToArray());
    }

    private async Task<StageDispatchResult> RunStageAsync(
        WorkItemEntity item,
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
        AuditLogDocument audit,
        CancellationToken cancellationToken)
    {
        item.CurrentInputVersion = Math.Max(1, item.CurrentInputVersion);
        if (deploymentAction is null || deploymentAction == "Deploy")
        {
            // new task id per stage run (Finalize gets its own too)
        }

        var taskId = Guid.CreateVersion7().ToString("N");
        item.CurrentTaskId = taskId;
        item.CurrentStage = stage;
        item.Status = WorkItemStatuses.Running;
        item.PendingMessage = $"{stage}:{taskId}:{item.CurrentInputVersion}";
        item.PendingAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(item, stage, WorkItemStatuses.Running, "StageStarted", $"Running {stage}", taskId, cancellationToken)
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
            config = new { modelId = "fixture", reasoningEffort = "low" }
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
            await RegisterArtifactAsync(item, pair.Key, 1, pair.Value, cancellationToken).ConfigureAwait(false);
        }

        item.LastAppliedResultId = $"result:{taskId}:{item.CurrentInputVersion}";
        item.PendingMessage = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(
                item,
                stage,
                "Completed",
                "StageCompleted",
                result.Outputs?.GetValueOrDefault("deployedUrls") ?? $"{stage} completed",
                taskId,
                cancellationToken)
            .ConfigureAwait(false);

        audit.Transitions.Add(new AuditTransition
        {
            Stage = stage + (deploymentAction is null ? string.Empty : ":" + deploymentAction),
            Status = "Completed",
            At = DateTimeOffset.UtcNow,
            Message = $"task {taskId}",
            ArtifactPath = result.Artifacts?.Values.FirstOrDefault()
        });
        await WriteAuditAsync(item, audit, cancellationToken).ConfigureAwait(false);

        return new StageDispatchResult(
            taskId,
            result.Artifacts ?? new Dictionary<string, string>(),
            result.Outputs ?? new Dictionary<string, string>());
    }

    private async Task AutoApproveAsync(
        WorkItemEntity item,
        string stage,
        AuditLogDocument audit,
        CancellationToken cancellationToken)
    {
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
        await UpsertWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(
                item,
                stage,
                "Approved",
                "GateApproved",
                $"Auto-approved by {_options.DummyUserId}",
                item.CurrentTaskId,
                cancellationToken)
            .ConfigureAwait(false);
        audit.Transitions.Add(new AuditTransition
        {
            Stage = stage,
            Status = "Approved",
            At = DateTimeOffset.UtcNow,
            Message = $"causationId=gate:{approval.RowKey}:Approved"
        });
        await WriteAuditAsync(item, audit, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendEventAsync(
        WorkItemEntity item,
        string stage,
        string status,
        string eventType,
        string message,
        string? taskId,
        CancellationToken cancellationToken)
    {
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
        _memoryEvents.Add(entity);
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
    }

    private async Task RegisterArtifactAsync(
        WorkItemEntity item,
        string key,
        int revision,
        string path,
        CancellationToken cancellationToken)
    {
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
        if (!_options.UseInMemoryTables)
        {
            var client = _tables.GetClient(TableNames.Artifacts);
            await client.UpsertEntityAsync(row, cancellationToken: cancellationToken).ConfigureAwait(false);
            await client.UpsertEntityAsync(head, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await Task.CompletedTask;
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
        // release ~ACTIVE if present
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

    private async Task WriteAuditAsync(
        WorkItemEntity item,
        AuditLogDocument audit,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.AuditLogDirectory);
        var path = AuditLogPath(item);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(audit, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        // also mirror under work-item relative path name for blob-style layout
        var blobStyle = Path.Combine(
            _options.AuditLogDirectory,
            item.ProjectId,
            item.WorkItemId,
            "v1",
            "AuditLog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(blobStyle)!);
        await File.WriteAllTextAsync(blobStyle, json, cancellationToken).ConfigureAwait(false);
    }

    private string AuditLogPath(WorkItemEntity item) =>
        Path.Combine(_options.AuditLogDirectory, $"{item.WorkItemId}-AuditLog.json");

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
}

public sealed class PocPipelineStartRequest
{
    public string UserRequest { get; set; } = string.Empty;
    public string? StackProfile { get; set; }
    public string? ProjectMode { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public Dictionary<string, string>? BaselineArtifacts { get; set; }
}

public sealed record PocPipelineRunResult(
    string ProjectId,
    string WorkItemId,
    string Status,
    string Stage,
    string? DeployedUrl,
    string AuditLogPath,
    IReadOnlyList<string> TransitionTrail,
    IReadOnlyList<string> EventTypes);

public sealed class AuditLogDocument
{
    public string OriginalRequest { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string StackProfile { get; set; } = string.Empty;
    public string ProjectMode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FinalStatus { get; set; }
    public string? DeployedUrl { get; set; }
    public List<AuditTransition> Transitions { get; set; } = [];
}

public sealed class AuditTransition
{
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ArtifactPath { get; set; }
}
