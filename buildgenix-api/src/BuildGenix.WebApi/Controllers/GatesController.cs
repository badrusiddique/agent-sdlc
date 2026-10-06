using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Route("api/work-items/{workItemId}")]
[Tags("Gates")]
public sealed class GatesController : ControllerBase
{
    private readonly IWorkItemRepository _workItems;

    public GatesController(IWorkItemRepository workItems) => _workItems = workItems;

    [HttpGet("gates")]
    [ProducesResponseType(typeof(GateListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListGatesAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new GateListResponse([]));
    }

    [HttpGet("approvals")]
    [ProducesResponseType(typeof(ApprovalListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListApprovalsAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new ApprovalListResponse([]));
    }

    [HttpPost("gates/{gateId}/decisions")]
    public async Task<IResult> DecideAsync(
        string workItemId,
        string gateId,
        [FromBody] GateDecisionRequest request,
        CancellationToken cancellationToken)
    {
        _ = gateId;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        if (string.IsNullOrWhiteSpace(request.Decision)
            || string.IsNullOrWhiteSpace(request.ArtifactId)
            || request.ArtifactRevision <= 0)
        {
            return ApiProblems.ValidationFailed("decision, artifactId, and artifactRevision are required.");
        }

        return ApiProblems.NotReady("Gate decision persistence is not wired in this slice.");
    }

    [HttpGet("assumptions")]
    [ProducesResponseType(typeof(AssumptionListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListAssumptionsAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new AssumptionListResponse([]));
    }

    [HttpPatch("findings/{findingId}")]
    public async Task<IResult> UpdateFindingAsync(
        string workItemId,
        string findingId,
        [FromBody] UpdateFindingStateRequest request,
        CancellationToken cancellationToken)
    {
        _ = findingId;
        _ = request;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return ApiProblems.NotReady("FindingStates are planned B3.");
    }

    private async Task<IResult?> MissingAsync(string workItemId, CancellationToken cancellationToken)
    {
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        return item is null ? ApiProblems.NotFound($"Work item '{workItemId}' was not found.") : null;
    }
}
