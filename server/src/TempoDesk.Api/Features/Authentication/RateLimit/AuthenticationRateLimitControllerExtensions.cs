using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

internal static class AuthenticationRateLimitControllerExtensions
{
    public static async Task<IActionResult?> EnforceRateLimitAsync(
        this ControllerBase controller,
        IAuthenticationRateLimiter limiter,
        IAuthenticationAuditService audit,
        AuthenticationRateLimitPolicy policy,
        string? email,
        CancellationToken cancellationToken)
    {
        var result = limiter.Acquire(new AuthenticationRateLimitRequest(
            policy,
            controller.HttpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            email is null
                ? null
                : AuthenticationRateLimitKeys.ForAccount(email)));
        if (result.Allowed)
            return null;

        await audit.RecordRateLimitedAsync(policy, cancellationToken);
        controller.Response.Headers.RetryAfter = Math.Max(
            1,
            (int)Math.Ceiling(result.RetryAfter.TotalSeconds)).ToString(
                CultureInfo.InvariantCulture);
        return controller.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too Many Requests",
            detail: "Too many authentication attempts. Try again later.");
    }
}
