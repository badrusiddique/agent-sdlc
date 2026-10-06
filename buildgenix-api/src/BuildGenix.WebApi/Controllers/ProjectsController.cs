using BuildGenix.Data.Entities;
using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Auth;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Route("api/projects")]
[Tags("Projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projects;

    public ProjectsController(IProjectRepository projects) => _projects = projects;

    [HttpGet]
    [ProducesResponseType(typeof(ProjectListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectListResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = await _projects.ListAsync(cancellationToken).ConfigureAwait(false);
        var items = entities.Select(MapSummary).ToList();
        return Ok(new ProjectListResponse(items));
    }

    [HttpGet("{projectId}")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetAsync(string projectId, CancellationToken cancellationToken)
    {
        var entity = await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        return entity is null
            ? ApiProblems.NotFound($"Project '{projectId}' was not found.")
            : Results.Ok(MapDetail(entity));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateProjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateAsync(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiProblems.ValidationFailed("Name is required.");
        }

        var projectId = Guid.CreateVersion7().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var entity = new ProjectEntity
        {
            RowKey = projectId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedBy = HttpContext.GetUserId(),
            CreatedAt = now
        };

        await _projects.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
        var response = new CreateProjectResponse(projectId, entity.Name, now);
        return Results.Created($"/api/projects/{projectId}", response);
    }

    [HttpPatch("{projectId}")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> PatchAsync(
        string projectId,
        [FromBody] PatchProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Description is null && request.Settings is null)
        {
            return ApiProblems.ValidationFailed("At least one field is required.");
        }

        // SettingsJson planned B3 — do not silently accept settings.
        if (request.Settings is not null)
        {
            return ApiProblems.NotReady("Project settings persistence is planned B3.");
        }

        var entity = await _projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return ApiProblems.NotFound($"Project '{projectId}' was not found.");
        }

        entity.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        await _projects.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
        return Results.Ok(MapDetail(entity));
    }

    private static ProjectSummaryDto MapSummary(ProjectEntity e) =>
        new(e.RowKey, e.Name, e.Description, e.CreatedAt, e.CreatedBy);

    private static ProjectDetailDto MapDetail(ProjectEntity e) =>
        new(e.RowKey, e.Name, e.Description, e.CreatedAt, e.CreatedBy);
}
