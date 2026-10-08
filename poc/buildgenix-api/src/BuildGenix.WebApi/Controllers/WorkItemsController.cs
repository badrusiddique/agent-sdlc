using BuildGenix.Data.Entities;
using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Auth;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Tags("WorkItems")]
public sealed class WorkItemsController : ControllerBase
{
    public const string IntakeThreadId = "I01";

    private readonly IProjectRepository _projects;
    private readonly IWorkItemRepository _workItems;

    public WorkItemsController(IProjectRepository projects, IWorkItemRepository workItems)
    {
        _projects = projects;
        _workItems = workItems;
    }

    [HttpGet("api/projects/{projectId}/work-items")]
    [ProducesResponseType(typeof(WorkItemListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> ListAsync(
        string projectId,
        [FromQuery] string? statusFilter,
        [FromQuery] int? take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        _ = take;
        _ = continuationToken;
        var project = await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        var items = await _workItems.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var filtered = ApplyStatusFilter(items, statusFilter)
            .Select(MapListItem)
            .ToList();
        return Results.Ok(new WorkItemListResponse(filtered));
    }

    [HttpGet("api/work-items/{workItemId}")]
    [ProducesResponseType(typeof(WorkItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetAsync(string workItemId, CancellationToken cancellationToken)
    {
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        return item is null
            ? ApiProblems.NotFound($"Work item '{workItemId}' was not found.")
            : Results.Ok(MapDetail(item));
    }

    /// <summary>Create WI record only (D-C4). Does not take ~ACTIVE (D-C4b). Does not start pipeline.</summary>
    [HttpPost("api/projects/{projectId}/work-items")]
    [ProducesResponseType(typeof(CreateWorkItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> CreateAsync(
        string projectId,
        [FromBody] CreateWorkItemRequest? request,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        var idempotencyKey = request?.IdempotencyKey
                             ?? Request.Headers["Idempotency-Key"].FirstOrDefault();

        var result = await _workItems.TryCreateAsync(
            projectId,
            project.Name,
            HttpContext.GetUserId(),
            idempotencyKey,
            request?.Title,
            cancellationToken).ConfigureAwait(false);

        var item = result.Item!;
        var response = new CreateWorkItemResponse(
            item.WorkItemId,
            item.DisplayNumber,
            item.Status,
            item.ProjectId,
            IntakeThreadId);

        if (result.Outcome == WorkItemCreateOutcome.Duplicate)
        {
            return Results.Ok(response);
        }

        return Results.Created($"/api/work-items/{item.WorkItemId}", response);
    }

    /// <summary>Implement now (D-C4). Engages ~ACTIVE (D-C4b). Same summaryRevision => no-op (D-C5).</summary>
    [HttpPost("api/work-items/{workItemId}/execute")]
    [ProducesResponseType(typeof(ExecuteWorkItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IResult> ExecuteAsync(
        string workItemId,
        [FromBody] ExecuteWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workItems.TryImplementAsync(workItemId, request.SummaryRevision, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            WorkItemImplementOutcome.NotFound =>
                ApiProblems.NotFound($"Work item '{workItemId}' was not found."),
            WorkItemImplementOutcome.InvalidRevision =>
                ApiProblems.ValidationFailed("summaryRevision must be greater than 0."),
            WorkItemImplementOutcome.SummaryNotFound =>
                ApiProblems.SummaryNotFound(
                    $"No committed intake summary for summaryRevision {request.SummaryRevision}."),
            WorkItemImplementOutcome.ActiveConflict =>
                ApiProblems.ActiveConflict(
                    "Another work item holds the project active lock.",
                    result.ActiveWorkItemId,
                    result.ActiveDisplayNumber),
            WorkItemImplementOutcome.SameWiDifferentRevisionOpen =>
                ApiProblems.ValidationFailed(
                    "Same work item with a different summaryRevision while the pipeline is non-terminal is not decided (I1-R1)."),
            WorkItemImplementOutcome.AlreadyStarted or WorkItemImplementOutcome.Started =>
                Results.Ok(new ExecuteWorkItemResponse(
                    result.Item!.WorkItemId,
                    result.Item.Status,
                    result.Item.CurrentStage,
                    result.Item.BoundSummaryRevision,
                    AlreadyStarted: result.Outcome == WorkItemImplementOutcome.AlreadyStarted)),
            _ => ApiProblems.ValidationFailed("Unexpected implement outcome.")
        };
    }

    [HttpPost("api/work-items/{workItemId}/answer")]
    [ProducesResponseType(typeof(WorkItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IResult Answer(string workItemId, [FromBody] AnswerHumanActionRequest request)
    {
        _ = workItemId;
        if (string.IsNullOrWhiteSpace(request.Answer))
        {
            return ApiProblems.ValidationFailed("answer is required.");
        }

        return ApiProblems.NotReady("HumanActionRequired answer persistence is not wired yet (needs results/gate path).");
    }

    [HttpPost("api/work-items/{workItemId}/pause")]
    [ProducesResponseType(typeof(WorkItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IResult Pause(string workItemId, [FromBody] PauseWorkItemRequest? request)
    {
        _ = workItemId;
        _ = request;
        return ApiProblems.NotReady("Pause semantics are OPEN (I1 contracts).");
    }

    [HttpPost("api/work-items/{workItemId}/stop")]
    [ProducesResponseType(typeof(WorkItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IResult Stop(string workItemId, [FromBody] StopWorkItemRequest? request)
    {
        _ = workItemId;
        _ = request;
        return ApiProblems.NotReady("Stop semantics are OPEN (I1 contracts).");
    }

    [HttpGet("api/work-items/{workItemId}/progress")]
    [ProducesResponseType(typeof(ProgressResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ProgressAsync(
        string workItemId,
        [FromQuery] string? stageCode,
        CancellationToken cancellationToken)
    {
        _ = stageCode;
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        // StageStatuses planned B2a; Draft/pre-Implement returns empty stages (no INTAKE stage code).
        return Results.Ok(new ProgressResponse(
            Stages: [],
            SelectedStage: null,
            WaitingOnYou: [],
            StillToCome: [],
            ApprovalsBrief: null));
    }

    [HttpGet("api/work-items/{workItemId}/telemetry")]
    [ProducesResponseType(typeof(WorkItemTelemetryResponse), StatusCodes.Status200OK)]
    public async Task<IResult> TelemetryAsync(string workItemId, CancellationToken cancellationToken)
    {
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        return Results.Ok(new WorkItemTelemetryResponse([], 0, 0, 0, 0, 0));
    }

    private static IEnumerable<WorkItemEntity> ApplyStatusFilter(
        IReadOnlyList<WorkItemEntity> items,
        string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter) || statusFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return items;
        }

        return statusFilter.ToLowerInvariant() switch
        {
            "complete" => items.Where(i => i.Status == WorkItemStatuses.Completed),
            "blocked" => items.Where(i => i.Status == WorkItemStatuses.Failed),
            "waiting" => items.Where(i =>
                i.Status is WorkItemStatuses.AwaitingApproval or WorkItemStatuses.HumanActionRequired),
            "active" => items.Where(i =>
                i.PipelineStarted && !WorkItemStatuses.IsTerminal(i.Status)),
            _ => items
        };
    }

    private static WorkItemListItemDto MapListItem(WorkItemEntity e) =>
        new(e.WorkItemId, e.DisplayNumber, e.Title, e.Status, e.CurrentStage, null, null, null);

    private static WorkItemDetailDto MapDetail(WorkItemEntity e) =>
        new(
            e.WorkItemId,
            e.ProjectId,
            e.ProjectName,
            e.DisplayNumber,
            e.Title,
            e.Status,
            e.CurrentStage,
            e.CreatedAt,
            e.UpdatedAt,
            e.HumanMessage,
            e.BlobPrefix);
}
