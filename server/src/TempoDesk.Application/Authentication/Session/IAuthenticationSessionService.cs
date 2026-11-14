namespace TempoDesk.Application.Authentication;

public interface IAuthenticationSessionService
{
    Task LogoutAsync(
        string rawToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthenticationSession>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task RevokeAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
