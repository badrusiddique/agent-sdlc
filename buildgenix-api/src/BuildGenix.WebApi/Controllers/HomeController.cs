using BuildGenix.WebApi.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace BuildGenix.WebApi.Controllers;

[ApiController]
[Route("api")]
[Tags("Home")]
public sealed class HomeController : ControllerBase
{
    /// <summary>Waiting-on-you queue. Backed by PendingApprovals (planned B2a); empty until that table lands.</summary>
    [HttpGet("pending-actions")]
    [ProducesResponseType(typeof(PendingActionListResponse), StatusCodes.Status200OK)]
    public ActionResult<PendingActionListResponse> PendingActions(
        [FromQuery] string? projectId,
        [FromQuery] int? take,
        [FromQuery] string? sort)
    {
        _ = projectId;
        _ = take;
        _ = sort;
        return Ok(new PendingActionListResponse(0, []));
    }

    /// <summary>Home recent activity glance. Backed by ProjectEvents (planned B2a).</summary>
    [HttpGet("home/activity")]
    [ProducesResponseType(typeof(HomeActivityResponse), StatusCodes.Status200OK)]
    public ActionResult<HomeActivityResponse> HomeActivity([FromQuery] int? take)
    {
        _ = take;
        return Ok(new HomeActivityResponse([]));
    }
}
