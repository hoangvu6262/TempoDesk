using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AuthenticationTokenService(
    IRefreshTokenRepository refreshTokens,
    AccessTokenIssuer accessTokens,
    IOptions<AuthenticationOptions> configured,
    TimeProvider timeProvider) : IAuthenticationTokenService
{
    public async Task<AuthenticationTokenResult> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var rawToken = CreateToken();
        var expiresAt = now.AddDays(configured.Value.RefreshTokenDays);
        await refreshTokens.CreateAsync(
            userId,
            Guid.NewGuid(),
            new NewRefreshToken(
                Guid.NewGuid(), HashToken(rawToken), expiresAt, now),
            cancellationToken);
        var accessToken = accessTokens.Issue(userId);
        return new AuthenticationTokenResult(
            accessToken.Value,
            accessToken.ExpiresIn,
            rawToken,
            expiresAt);
    }

    public Task<AuthenticationTokenResult?> RefreshAsync(
        string rawToken,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthenticationTokenResult?>(null);

    private static string CreateToken() => Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string HashToken(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
