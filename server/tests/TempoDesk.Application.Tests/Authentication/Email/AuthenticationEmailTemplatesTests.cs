using TempoDesk.Application.Authentication;

namespace TempoDesk.Application.Tests;

public sealed class AuthenticationEmailTemplatesTests
{
    [Fact]
    public void VerificationContainsSubjectCopyAndEncodedFrontendLink()
    {
        var content = AuthenticationEmailTemplates.Verification(
            new Uri("https://app.example/tempo"), "token/+?");
        const string link =
            "https://app.example/tempo/verify-email?token=token%2F%2B%3F";

        Assert.Equal("Verify your TempoDesk email", content.Subject);
        Assert.Contains("Verify your email", content.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(link, content.TextBody, StringComparison.Ordinal);
        Assert.Contains("Verify email", content.HtmlBody,
            StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", content.HtmlBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordResetContainsSubjectCopyAndEncodedFrontendLink()
    {
        var content = AuthenticationEmailTemplates.PasswordReset(
            new Uri("https://app.example/tempo/"), "reset/+?");
        const string link =
            "https://app.example/tempo/reset-password?token=reset%2F%2B%3F";

        Assert.Equal("Reset your TempoDesk password", content.Subject);
        Assert.Contains("Reset your password", content.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(link, content.TextBody, StringComparison.Ordinal);
        Assert.Contains("Reset password", content.HtmlBody,
            StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", content.HtmlBody,
            StringComparison.Ordinal);
    }
}
