namespace TempoDesk.Application.Authentication;

public sealed record LoginResult(
    string? AccessToken,
    int ExpiresIn,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null)
{
    public bool Succeeded => AccessToken is not null;

    public static LoginResult Rejected { get; } = new(null, 0);
}
