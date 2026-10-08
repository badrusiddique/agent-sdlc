using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Poc;

[ApiController]
[Route("api/poc/pipeline")]
public sealed class PocPipelineController : ControllerBase
{
    private readonly PocPipelineOrchestrator _orchestrator;

    public PocPipelineController(PocPipelineOrchestrator orchestrator) => _orchestrator = orchestrator;

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
}
