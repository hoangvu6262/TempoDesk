using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth/sessions")]
public sealed class SessionsController(
    IAuthenticationSessionService sessions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        CancellationToken cancellationToken)
    {
        var result = await sessions.ListAsync(UserId(), cancellationToken);
        return Ok(result.Select(session => new SessionResponseDto(
            session.Id,
            session.UserAgent,
            session.IpAddress,
            session.CreatedAt,
            session.LastUsedAt,
            session.ExpiresAt,
            session.Status.ToString().ToLowerInvariant())));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(
        Guid id,
        CancellationToken cancellationToken) =>
        await sessions.RevokeAsync(UserId(), id, cancellationToken)
            ? NoContent()
            : NotFound();

    [HttpDelete]
    public async Task<IActionResult> RevokeAll(
        CancellationToken cancellationToken)
    {
        await sessions.RevokeAllAsync(UserId(), cancellationToken);
        return NoContent();
    }

    private Guid UserId() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
