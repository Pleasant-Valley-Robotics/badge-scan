using System.Security.Claims;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadgeScan.Web.Controllers.Api;

/// <summary>
/// "Who am I to you?" A program checks its data key works with GET /api/me before syncing:
/// { "kind": "program", "name": "Project-Tracker" }. 401 means the key is wrong or revoked.
/// </summary>
[ApiController]
[Route("api/me")]
public sealed class MeApiController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = SignIns.Policies.RecentScans)]
    public IActionResult Get() => Ok(new
    {
        kind = User.FindFirstValue(SignIns.Kind),
        name = User.FindFirstValue(ClaimTypes.Name),
    });
}
