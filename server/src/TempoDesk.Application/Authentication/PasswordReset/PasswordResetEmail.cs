namespace TempoDesk.Application.Authentication;

public sealed record PasswordResetEmail(string Email, string Token);
