using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class PasswordResetController(
    IAccountPasswordResetService resets) : ControllerBase
{
    private static readonly object GenericAcceptedResponse = new
    {
        message = "If the account exists, a password reset email will be sent."
    };

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        await resets.ForgotPasswordAsync(request.Email, cancellationToken);
        return Accepted(GenericAcceptedResponse);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await resets.ResetPasswordAsync(
            request.Token, request.NewPassword, cancellationToken);
        if (result.Succeeded)
            return NoContent();

        if (result.TokenRejected)
            ModelState.AddModelError(
                "token", "The reset token is invalid or expired.");
        else
            foreach (var error in result.Errors)
                foreach (var message in error.Value)
                    ModelState.AddModelError(error.Key, message);

        return ValidationProblem(ModelState);
    }
}
