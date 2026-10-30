using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class LoginController(IAccountLoginService login) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(
            request.Email, request.Password, cancellationToken);
        if (!result.Succeeded)
            return Unauthorized();

        return Ok(new LoginResponse(
            result.AccessToken!, "Bearer", result.ExpiresIn));
    }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MaxLength(1024)]
    public string Password { get; init; } = "";
}

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn);
