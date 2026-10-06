using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Route("api/work-items/{workItemId}")]
[Tags("Activity")]
public sealed class ActivityController : ControllerBase
{
    private readonly IWorkItemRepository _workItems;

    public ActivityController(IWorkItemRepository workItems) => _workItems = workItems;

    [HttpGet("activity")]
    [ProducesResponseType(typeof(ActivityListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListAsync(
        string workItemId,
        [FromQuery] string? filter,
        [FromQuery] int? take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        _ = filter;
        _ = take;
        _ = continuationToken;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new ActivityListResponse(0, 0, 0, []));
    }

    [HttpGet("activity/{eventId}")]
    public async Task<IResult> GetAsync(string workItemId, string eventId, CancellationToken cancellationToken)
    {
        _ = eventId;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return ApiProblems.NotFound($"Activity event '{eventId}' was not found.");
    }

    [HttpGet("runs/{runId}/log")]
    [ProducesResponseType(typeof(RunLogResponse), StatusCodes.Status200OK)]
    public async Task<IResult> RunLogAsync(
        string workItemId,
        string runId,
        [FromQuery] long? fromOffsetMs,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        _ = fromOffsetMs;
        _ = take;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new RunLogResponse(runId, []));
    }

    [HttpGet("activity/export")]
    public async Task<IResult> ExportAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return ApiProblems.NotReady("Activity export format is OPEN.");
    }

    private async Task<IResult?> MissingAsync(string workItemId, CancellationToken cancellationToken)
    {
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        return item is null ? ApiProblems.NotFound($"Work item '{workItemId}' was not found.") : null;
    }
}
