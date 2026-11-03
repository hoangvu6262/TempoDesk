namespace TempoDesk.Application.Authentication;

public interface IAccountLoginService
{
    Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}
