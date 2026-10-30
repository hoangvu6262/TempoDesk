namespace TempoDesk.Application.Authentication;

public interface IAccountLoginService
{
    Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record LoginResult(string? AccessToken, int ExpiresIn)
{
    public bool Succeeded => AccessToken is not null;

    public static LoginResult Rejected { get; } = new(null, 0);
}
