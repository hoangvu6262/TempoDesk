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
        SessionMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var rawToken = CreateToken();
        var token = NewToken(rawToken, now);
        await refreshTokens.CreateAsync(
            userId,
            Guid.NewGuid(),
            token,
            metadata,
            cancellationToken);
        var accessToken = accessTokens.Issue(userId);
        return new AuthenticationTokenResult(
            accessToken.Value,
            accessToken.ExpiresIn,
            rawToken,
            token.ExpiresAt);
    }

    public async Task<AuthenticationTokenResult?> RefreshAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (string.IsNullOrEmpty(rawToken) || rawToken.Length > 512)
        {
            await refreshTokens.RecordRejectedAsync(now, cancellationToken);
            return null;
        }

        var replacementRawToken = CreateToken();
        var replacement = NewToken(replacementRawToken, now);
        var rotation = await refreshTokens.RotateAsync(
            HashToken(rawToken), replacement, now, cancellationToken);
        if (rotation.Outcome != RefreshTokenRotation.Status.Succeeded ||
            rotation.UserId is null)
            return null;

        var accessToken = accessTokens.Issue(rotation.UserId.Value);
        return new AuthenticationTokenResult(
            accessToken.Value,
            accessToken.ExpiresIn,
            replacementRawToken,
            replacement.ExpiresAt);
    }

    private NewRefreshToken NewToken(
        string rawToken,
        DateTimeOffset now) => new(
        Guid.NewGuid(),
        HashToken(rawToken),
        now.AddDays(configured.Value.RefreshTokenDays),
        now);

    private static string CreateToken() => Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string HashToken(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
