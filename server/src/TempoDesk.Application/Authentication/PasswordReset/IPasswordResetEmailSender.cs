namespace TempoDesk.Application.Authentication;

public interface IPasswordResetEmailSender
{
    Task<bool> SendAsync(
        PasswordResetEmail message,
        CancellationToken cancellationToken);
}
