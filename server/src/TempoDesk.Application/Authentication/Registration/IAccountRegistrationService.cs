namespace TempoDesk.Application.Authentication;

public interface IAccountRegistrationService
{
    Task<RegistrationResult> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task ResendVerificationAsync(
        string email,
        CancellationToken cancellationToken = default);
}
