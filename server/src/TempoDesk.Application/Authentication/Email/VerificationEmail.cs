namespace TempoDesk.Application.Authentication;

public sealed record VerificationEmail(
    string Email,
    string Token,
    DateTimeOffset ExpiresAt);
