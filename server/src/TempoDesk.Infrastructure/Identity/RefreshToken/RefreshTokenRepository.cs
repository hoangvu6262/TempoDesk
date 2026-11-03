using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class RefreshTokenRepository(
    TempoDeskIdentityDbContext context) : IRefreshTokenRepository
{
    public async Task CreateAsync(
        Guid userId,
        Guid familyId,
        NewRefreshToken token,
        CancellationToken cancellationToken = default)
    {
        context.RefreshTokens.Add(new RefreshToken
        {
            Id = token.Id,
            UserId = userId,
            FamilyId = familyId,
            TokenHash = token.TokenHash,
            ExpiresAt = token.ExpiresAt,
            CreatedAt = token.CreatedAt
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshTokenRotation> RotateAsync(
        string tokenHash,
        NewRefreshToken replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) => Task.FromResult(
        new RefreshTokenRotation(
            RefreshTokenRotation.Status.Missing, null));

    public async Task RecordRejectedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        AddAudit("token.refresh", false, null, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(
        string eventType,
        bool succeeded,
        Guid? userId,
        DateTimeOffset occurredAt) => context.SecurityAuditLogs.Add(
        new SecurityAuditLog
        {
            UserId = userId,
            EventType = eventType,
            Succeeded = succeeded,
            OccurredAt = occurredAt
        });
}
