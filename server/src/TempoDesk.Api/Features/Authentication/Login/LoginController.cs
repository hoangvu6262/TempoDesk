using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class LoginController(IAccountLoginService login) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var metadata = new SessionMetadata(
            string.IsNullOrWhiteSpace(userAgent)
                ? null
                : userAgent[..Math.Min(userAgent.Length, 1024)],
            HttpContext.Connection.RemoteIpAddress);
        var result = await login.LoginAsync(
            request.Email, request.Password, metadata, cancellationToken);
        if (!result.Succeeded)
            return Unauthorized();

        if (result.RefreshToken is not null &&
            result.RefreshTokenExpiresAt is not null)
            RefreshTokenCookie.Write(
                Response,
                Request.IsHttps,
                result.RefreshToken,
                result.RefreshTokenExpiresAt.Value);

        return Ok(new AccessTokenResponseDto(
            result.AccessToken!, "Bearer", result.ExpiresIn));
    }
}
