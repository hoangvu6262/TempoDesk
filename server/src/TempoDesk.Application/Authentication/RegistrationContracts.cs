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

public sealed record RegistrationResult(
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static RegistrationResult Accepted { get; } =
        new(new Dictionary<string, string[]>());
}

public sealed record VerificationEmail(
    string Email,
    string Token,
    DateTimeOffset ExpiresAt);

public interface IEmailVerificationSender
{
    Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken);
}
