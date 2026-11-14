namespace TempoDesk.Application.Authentication;

public interface IAccountPasswordResetService
{
    Task ForgotPasswordAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<ResetPasswordResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}
