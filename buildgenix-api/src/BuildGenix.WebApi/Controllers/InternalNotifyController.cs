using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

/// <summary>
/// Optional internal REST notify (D-CL-7 alternate). Not for browsers.
/// Typed envelopes; not implemented (Phase 0 default is Service Bus).
/// </summary>
[ApiController]
[Route("api/internal")]
[Tags("Internal")]
public sealed class InternalNotifyController : ControllerBase
{
    [HttpPost("sdlc-results")]
    [ProducesResponseType(typeof(AcceptedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IResult SdlcResults([FromBody] SdlcResultEnvelope envelope)
    {
        _ = envelope;
        return ApiProblems.NotReady("Internal REST notify is optional; Phase 0 default is Service Bus only.");
    }

    [HttpPost("sdlc-events")]
    [ProducesResponseType(typeof(AcceptedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IResult SdlcEvents([FromBody] SdlcEventEnvelope envelope)
    {
        _ = envelope;
        return ApiProblems.NotReady("Internal REST notify is optional; Phase 0 default is Service Bus only.");
    }
}
