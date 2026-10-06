using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

/// <summary>
/// Optional internal REST notify (D-CL-7 alternate). Not for browsers.
/// Phase 0 default ingress remains Service Bus; these return 501 until isolation is designed.
/// </summary>
[ApiController]
[Route("api/internal")]
[Tags("Internal")]
public sealed class InternalNotifyController : ControllerBase
{
    [HttpPost("sdlc-results")]
    public IResult SdlcResults([FromBody] object? envelope)
    {
        _ = envelope;
        return ApiProblems.NotReady("Internal REST notify is optional; Phase 0 default is Service Bus only.");
    }

    [HttpPost("sdlc-events")]
    public IResult SdlcEvents([FromBody] object? envelope)
    {
        _ = envelope;
        return ApiProblems.NotReady("Internal REST notify is optional; Phase 0 default is Service Bus only.");
    }
}
