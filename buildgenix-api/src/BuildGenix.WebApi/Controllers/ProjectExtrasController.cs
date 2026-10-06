using BuildGenix.Data.Repositories;
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
    public async Task<IResult> HumanActionsAsync(string projectId, CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        return Results.Ok(new { items = Array.Empty<object>() });
    }

    [HttpGet("documents")]
    public async Task<IResult> ListDocumentsAsync(string projectId, CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        return Results.Ok(new { items = Array.Empty<object>() });
    }

    [HttpPost("documents")]
    public async Task<IResult> AddDocumentAsync(string projectId, CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        return ApiProblems.NotReady("Application library documents are later E2.");
    }
}
