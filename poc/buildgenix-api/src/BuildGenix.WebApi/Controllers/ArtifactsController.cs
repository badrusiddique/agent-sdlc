using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Tags("Artifacts")]
public sealed class ArtifactsController : ControllerBase
{
    private readonly IWorkItemRepository _workItems;
    private readonly IProjectRepository _projects;

    public ArtifactsController(IWorkItemRepository workItems, IProjectRepository projects)
    {
        _workItems = workItems;
        _projects = projects;
    }

    [HttpGet("api/work-items/{workItemId}/artifacts")]
    [ProducesResponseType(typeof(ArtifactListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListForWorkItemAsync(
        string workItemId,
        [FromQuery] string? groupBy,
        [FromQuery] bool? currentOnly,
        CancellationToken cancellationToken)
    {
        _ = groupBy;
        _ = currentOnly;
        if (await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        return Results.Ok(new ArtifactListResponse([]));
    }

    [HttpGet("api/work-items/{workItemId}/artifacts/{artifactId}")]
    [ProducesResponseType(typeof(ArtifactDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetAsync(string workItemId, string artifactId, CancellationToken cancellationToken)
    {
        if (await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        return ApiProblems.NotFound($"Artifact '{artifactId}' was not found.");
    }

    [HttpGet("api/work-items/{workItemId}/artifacts/{artifactId}/content")]
    [ProducesResponseType(typeof(ArtifactContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> ContentAsync(string workItemId, string artifactId, CancellationToken cancellationToken)
    {
        if (await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        return ApiProblems.NotFound($"Artifact '{artifactId}' content was not found.");
    }

    [HttpGet("api/projects/{projectId}/artifacts")]
    [ProducesResponseType(typeof(ProjectArtifactListResponse), StatusCodes.Status200OK)]
    public async Task<IResult> ListForProjectAsync(
        string projectId,
        [FromQuery] int? take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        _ = take;
        _ = continuationToken;
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        return Results.Ok(new ProjectArtifactListResponse([]));
    }
}
