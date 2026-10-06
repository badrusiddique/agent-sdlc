using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

/// <summary>Steering proposals/interventions (later E1). Typed DTOs; not implemented.</summary>
[ApiController]
[Route("api/work-items/{workItemId}")]
[Tags("Steering")]
public sealed class SteeringController : ControllerBase
{
    private readonly IWorkItemRepository _workItems;

    public SteeringController(IWorkItemRepository workItems) => _workItems = workItems;

    [HttpGet("proposals")]
    [ProducesResponseType(typeof(ProposalListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListProposalsAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new ProposalListResponse([]));
    }

    [HttpPost("proposals/{proposalId}/decisions")]
    [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IResult> DecideProposalAsync(
        string workItemId,
        string proposalId,
        [FromBody] ProposalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        _ = proposalId;
        _ = request;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return ApiProblems.NotReady("Steering proposals are later E1.");
    }

    [HttpPost("proposals/bulk-decisions")]
    [ProducesResponseType(typeof(ProposalListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IResult> BulkDecideAsync(
        string workItemId,
        [FromBody] BulkProposalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return ApiProblems.NotReady("Steering bulk decisions are later E1.");
    }

    [HttpGet("interventions")]
    [ProducesResponseType(typeof(InterventionListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> InterventionsAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new InterventionListResponse([]));
    }

    [HttpGet("steering/cost")]
    [ProducesResponseType(typeof(SteeringCostResponse), StatusCodes.Status200OK)]
    public async Task<IResult> CostAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new SteeringCostResponse(0, 0, 0, 0, null));
    }

    private async Task<IResult?> MissingAsync(string workItemId, CancellationToken cancellationToken)
    {
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        return item is null ? ApiProblems.NotFound($"Work item '{workItemId}' was not found.") : null;
    }
}
