using System.Net;

namespace TempoDesk.Application.Authentication;

public sealed record EmailContent(
    string Subject,
    string TextBody,
    string HtmlBody);

public static class AuthenticationEmailTemplates
{
    public static EmailContent Verification(
        Uri frontendBaseUrl,
        string token) => Create(
            "Verify your TempoDesk email",
            "Verify your email",
            "Verify email",
            "If you did not create a TempoDesk account, ignore this email.",
            frontendBaseUrl,
            "verify-email",
            token);

    public static EmailContent PasswordReset(
        Uri frontendBaseUrl,
        string token) => Create(
            "Reset your TempoDesk password",
            "Reset your password",
            "Reset password",
            "If you did not request a password reset, ignore this email.",
            frontendBaseUrl,
            "reset-password",
            token);

    private static EmailContent Create(
        string subject,
        string heading,
        string action,
        string fallback,
        Uri frontendBaseUrl,
        string route,
        string token)
    {
        ArgumentNullException.ThrowIfNull(frontendBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var link = $"{frontendBaseUrl.AbsoluteUri.TrimEnd('/')}/{route}" +
            $"?token={Uri.EscapeDataString(token)}";
        var encodedLink = WebUtility.HtmlEncode(link);

        return new EmailContent(
            subject,
            $"{heading}\n\n{link}\n\n{fallback}",
            $"<p>{heading}</p><p><a href=\"{encodedLink}\">{action}</a></p>" +
            $"<p>{fallback}</p>");
    }
}
