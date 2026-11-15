namespace TempoDesk.Application.Authentication;

public interface IRefreshTokenRepository
{
    Task CreateAsync(
        Guid userId,
        Guid familyId,
        NewRefreshToken token,
        SessionMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<RefreshTokenRotation> RotateAsync(
        string tokenHash,
        NewRefreshToken replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task RecordRejectedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        string? tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthenticationSession>> ListSessionsAsync(
        Guid userId,
        string? currentTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeSessionAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task RevokeAllSessionsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
