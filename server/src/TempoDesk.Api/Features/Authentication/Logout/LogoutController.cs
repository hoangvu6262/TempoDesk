using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class LogoutController(
    IAuthenticationSessionService sessions) : ControllerBase
{
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        await sessions.LogoutAsync(
            RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Delete(Response, Request.IsHttps);
        return NoContent();
    }
}
