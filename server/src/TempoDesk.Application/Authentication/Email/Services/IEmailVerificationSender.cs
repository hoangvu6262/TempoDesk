namespace TempoDesk.Application.Authentication;

public interface IEmailVerificationSender
{
    Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken);
}
