using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class RegistrationController(
    IAccountRegistrationService registration) : ControllerBase
{
    private static readonly object GenericAcceptedResponse = new
    {
        message = "If the address can be verified, an email will be sent."
    };

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await registration.RegisterAsync(
            request.Email, request.Password, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                foreach (var message in error.Value)
                    ModelState.AddModelError(error.Key, message);
            }

            return ValidationProblem(ModelState);
        }

        return Accepted(GenericAcceptedResponse);
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(
        VerifyEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        if (await registration.VerifyEmailAsync(request.Token, cancellationToken))
            return NoContent();

        ModelState.AddModelError(
            "token", "The verification token is invalid or expired.");
        return ValidationProblem(ModelState);
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(
        ResendVerificationRequestDto request,
        CancellationToken cancellationToken)
    {
        await registration.ResendVerificationAsync(
            request.Email, cancellationToken);
        return Accepted(GenericAcceptedResponse);
    }
}
