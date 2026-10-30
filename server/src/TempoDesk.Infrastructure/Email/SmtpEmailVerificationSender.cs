using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Email;

internal sealed class SmtpEmailVerificationSender(
    IOptions<EmailOptions> emailOptions) : IEmailVerificationSender
{
    private readonly EmailOptions options = emailOptions.Value;

    public async Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return false;

        var content = AuthenticationEmailTemplates.Verification(
            new Uri(options.FrontendBaseUrl), message.Token);
        using var email = new MailMessage
        {
            From = new MailAddress(options.From),
            Subject = content.Subject,
            Body = content.TextBody
        };
        email.To.Add(message.Email);
        email.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            content.TextBody, null, MediaTypeNames.Text.Plain));
        email.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            content.HtmlBody, null, MediaTypeNames.Text.Html));

        using var smtp = new SmtpClient(options.Host, options.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        await smtp.SendMailAsync(email, cancellationToken);
        return true;
    }
}
