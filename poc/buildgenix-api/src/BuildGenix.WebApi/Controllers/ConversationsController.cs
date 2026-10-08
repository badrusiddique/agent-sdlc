using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

/// <summary>
/// Conversation routes. Full transcript persistence remains B1; intake summary revision
/// markers are committed here so Implement can enforce the contracts Execute precondition.
/// </summary>
[ApiController]
[Route("api/work-items/{workItemId}/conversations")]
[Tags("Conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IWorkItemRepository _workItems;

    public ConversationsController(IWorkItemRepository workItems) => _workItems = workItems;

    [HttpGet]
    [ProducesResponseType(typeof(ConversationThreadListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> ListThreadsAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        var intake = new ConversationThreadSummaryDto(
            WorkItemsController.IntakeThreadId,
            "Intake",
            0,
            null,
            null);
        return Results.Ok(new ConversationThreadListResponse([intake]));
    }

    [HttpGet("{threadId}")]
    [ProducesResponseType(typeof(ConversationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetThreadAsync(string workItemId, string threadId, CancellationToken cancellationToken)
    {
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new ConversationDetailResponse(threadId, [], []));
    }

    [HttpPost("{threadId}/messages")]
    [ProducesResponseType(typeof(PostConversationMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IResult> PostMessageAsync(
        string workItemId,
        string threadId,
        [FromBody] PostConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        _ = threadId;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return ApiProblems.ValidationFailed("content is required.");
        }

        return ApiProblems.NotReady("Conversational intake persistence is planned B1.");
    }

    [HttpPost("{threadId}/attachments")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IResult> PostAttachmentAsync(
        string workItemId,
        string threadId,
        CancellationToken cancellationToken)
    {
        _ = threadId;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return ApiProblems.NotReady("Conversation attachments are planned B1.");
    }

    [HttpGet("{threadId}/context")]
    [ProducesResponseType(typeof(IntakeContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetContextAsync(string workItemId, string threadId, CancellationToken cancellationToken)
    {
        _ = threadId;
        if (await MissingAsync(workItemId, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        return Results.Ok(new IntakeContextDto([], [], [], [], []));
    }

    /// <summary>
    /// Commits the next intake summary revision marker (D-C3 filename intake-summary.json path).
    /// Full summary body persistence remains B1; the marker unblocks Implement precondition checks.
    /// </summary>
    [HttpPost("{threadId}/summaries")]
    [ProducesResponseType(typeof(IntakeSummaryRefDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> ProduceSummaryAsync(
        string workItemId,
        string threadId,
        [FromBody] ProduceIntakeSummaryRequest? request,
        CancellationToken cancellationToken)
    {
        _ = threadId;
        _ = request;
        var committed = await _workItems.TryCommitIntakeSummaryAsync(workItemId, cancellationToken)
            .ConfigureAwait(false);
        if (committed is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        return Results.Ok(new IntakeSummaryRefDto(
            committed.Revision,
            committed.ArtifactId,
            committed.BlobPath,
            committed.ProducedAt,
            MarkerOnly: true));
    }

    [HttpGet("{threadId}/summaries/{revision:int}")]
    [ProducesResponseType(typeof(IntakeSummaryDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetSummaryAsync(
        string workItemId,
        string threadId,
        int revision,
        CancellationToken cancellationToken)
    {
        _ = threadId;
        if (revision <= 0)
        {
            return ApiProblems.ValidationFailed("revision must be greater than 0.");
        }

        var summary = await _workItems.GetIntakeSummaryAsync(workItemId, revision, cancellationToken)
            .ConfigureAwait(false);
        if (summary is null)
        {
            return ApiProblems.NotFound($"Work item '{workItemId}' was not found.");
        }

        if (summary.Revision == 0)
        {
            return ApiProblems.NotFound($"Summary revision {revision} is not available.");
        }

        // Marker-only until B1 writes the D-C3 blob: do not advertise ContentUrl to a missing object.
        return Results.Ok(new IntakeSummaryDetailDto(
            summary.Revision,
            summary.ArtifactId,
            Content: null,
            ContentUrl: null,
            ProducedAt: summary.ProducedAt,
            MarkerOnly: true));
    }

    private async Task<IResult?> MissingAsync(string workItemId, CancellationToken cancellationToken)
    {
        var item = await _workItems.GetByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        return item is null ? ApiProblems.NotFound($"Work item '{workItemId}' was not found.") : null;
    }
}
