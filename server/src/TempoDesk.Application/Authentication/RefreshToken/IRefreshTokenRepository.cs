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
}
