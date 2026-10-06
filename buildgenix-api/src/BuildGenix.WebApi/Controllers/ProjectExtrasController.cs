using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Route("api/projects/{projectId}")]
[Tags("ProjectExtras")]
public sealed class ProjectExtrasController : ControllerBase
{
    private readonly IProjectRepository _projects;

    public ProjectExtrasController(IProjectRepository projects) => _projects = projects;

    [HttpGet("human-actions")]
    [ProducesResponseType(typeof(ProjectHumanActionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> HumanActionsAsync(string projectId, CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        return Results.Ok(new ProjectHumanActionListResponse([]));
    }

    [HttpGet("documents")]
    [ProducesResponseType(typeof(ProjectDocumentListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> ListDocumentsAsync(string projectId, CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        return Results.Ok(new ProjectDocumentListResponse([]));
    }

    [HttpPost("documents")]
    [ProducesResponseType(typeof(ProjectDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IResult> AddDocumentAsync(
        string projectId,
        [FromBody] RegisterProjectDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiProblems.ValidationFailed("name is required.");
        }

        return ApiProblems.NotReady("Application library documents are later E2.");
    }
}
