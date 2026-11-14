using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class RefreshTokenController(
    IAuthenticationTokenService tokens,
    IAuthenticationRateLimiter limiter,
    IAuthenticationAuditService audit) : ControllerBase
{
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken)
    {
        var limited = await this.EnforceRateLimitAsync(
            limiter, audit, AuthenticationRateLimitPolicy.Refresh,
            null, cancellationToken);
        if (limited is not null)
            return limited;

        var result = await tokens.RefreshAsync(
            RefreshTokenCookie.Read(Request), cancellationToken);
        if (result is null)
        {
            RefreshTokenCookie.Delete(Response, Request.IsHttps);
            return Unauthorized();
        }

        RefreshTokenCookie.Write(
            Response,
            Request.IsHttps,
            result.RefreshToken,
            result.RefreshTokenExpiresAt);
        return Ok(new AccessTokenResponseDto(
            result.AccessToken, "Bearer", result.ExpiresIn));
    }
}
