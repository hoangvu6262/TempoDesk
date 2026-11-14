using System.Security.Cryptography;
using System.Text;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AuthenticationSessionService(
    IRefreshTokenRepository refreshTokens,
    TimeProvider timeProvider) : IAuthenticationSessionService
{
    public Task LogoutAsync(
        string rawToken,
        CancellationToken cancellationToken = default) =>
        refreshTokens.LogoutAsync(
            string.IsNullOrEmpty(rawToken) || rawToken.Length > 512
                ? null
                : Hash(rawToken),
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<IReadOnlyList<AuthenticationSession>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        refreshTokens.ListSessionsAsync(
            userId, timeProvider.GetUtcNow(), cancellationToken);

    public Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        refreshTokens.RevokeSessionAsync(
            userId, sessionId, timeProvider.GetUtcNow(), cancellationToken);

    public Task RevokeAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        refreshTokens.RevokeAllSessionsAsync(
            userId, timeProvider.GetUtcNow(), cancellationToken);

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
