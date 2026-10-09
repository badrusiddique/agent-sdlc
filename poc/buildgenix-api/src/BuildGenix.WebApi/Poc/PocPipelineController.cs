using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Poc;

[ApiController]
[Route("api/poc/pipeline")]
public sealed class PocPipelineController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PocPipelineOrchestrator _orchestrator;

    public PocPipelineController(PocPipelineOrchestrator orchestrator) => _orchestrator = orchestrator;

    /// <summary>Sync happy path. Auto-approves gates (scripts / verify-three).</summary>
    [HttpPost("run")]
    [ProducesResponseType(typeof(PocPipelineRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PocPipelineRunResult>> RunAsync(
        [FromBody] PocPipelineStartRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            return BadRequest(new { error = "UserRequest is required." });
        }

        var result = await _orchestrator.RunAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Async interactive start for the chat UI. Pauses at human gates.</summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(PocPipelineStartResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PocPipelineStartResult>> StartAsync(
        [FromBody] PocPipelineStartRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            return BadRequest(new { error = "UserRequest is required." });
        }

        var result = await _orchestrator.StartAsync(request, cancellationToken).ConfigureAwait(false);
        return Accepted(result);
    }

    [HttpPost("{workItemId}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveAsync(
        string workItemId,
        [FromBody] PocApproveRequest? body,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orchestrator.ApproveAsync(workItemId, body?.Feedback, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown", StringComparison.Ordinal))
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{workItemId}/status")]
    [ProducesResponseType(typeof(PocPipelineStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<PocPipelineStatusDto> Status(string workItemId)
    {
        try
        {
            return Ok(_orchestrator.GetStatus(workItemId));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("{workItemId}/events")]
    [ProducesResponseType(typeof(IReadOnlyList<PocPipelineEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<PocPipelineEventDto>> Events(string workItemId, [FromQuery] int? after)
    {
        try
        {
            return Ok(_orchestrator.GetEvents(workItemId, after));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("{workItemId}/artifacts")]
    [ProducesResponseType(typeof(IReadOnlyList<PocArtifactListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<PocArtifactListItem>> Artifacts(string workItemId)
    {
        try
        {
            return Ok(_orchestrator.GetArtifacts(workItemId));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("{workItemId}/artifacts/{key}")]
    [ProducesResponseType(typeof(PocArtifactContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PocArtifactContentDto>> ArtifactContent(
        string workItemId,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _orchestrator
                .GetArtifactContentAsync(workItemId, key, cancellationToken)
                .ConfigureAwait(false);
            return content is null ? NotFound(new { error = "Artifact not found." }) : Ok(content);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("{workItemId}/logs")]
    [ProducesResponseType(typeof(IReadOnlyList<PocLogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<PocLogEntryDto>> Logs(string workItemId)
    {
        try
        {
            return Ok(_orchestrator.GetLogs(workItemId));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>Server-sent events: status snapshot on every pipeline state change.</summary>
    [HttpGet("{workItemId}/stream")]
    public async Task StreamAsync(string workItemId, CancellationToken cancellationToken)
    {
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("X-Accel-Buffering", "no");

        System.Threading.Channels.ChannelReader<PocPipelineStatusDto> reader;
        try
        {
            reader = _orchestrator.Subscribe(workItemId);
        }
        catch (InvalidOperationException ex)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await foreach (var status in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var payload = JsonSerializer.Serialize(status, JsonOptions);
                await Response.WriteAsync($"event: status\ndata: {payload}\n\n", cancellationToken)
                    .ConfigureAwait(false);
                await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (status.IsTerminal)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
    }
}
