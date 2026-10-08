using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Route("api/work-items/{workItemId}/sign-offs")]
[Tags("SignOffs")]
public sealed class SignOffsController : ControllerBase
{
    private readonly IWorkItemRepository _workItems;

    public SignOffsController(IWorkItemRepository workItems) => _workItems = workItems;

    [HttpPost]
    [ProducesResponseType(typeof(SignOffDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IResult> CreateAsync(
        string workItemId,
        [FromBody] CreateSignOffRequest request,
        CancellationToken cancellationToken)
    {
        if (await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.ArtifactId) || request.ArtifactRevision <= 0)
        {
            return ApiProblems.ValidationFailed("artifactId and artifactRevision are required.");
        }

        return ApiProblems.NotReady("Sign-offs are later E3.");
    }
}
