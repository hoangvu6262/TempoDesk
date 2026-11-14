using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class RefreshTokenRepository(
    TempoDeskIdentityDbContext context) : IRefreshTokenRepository
{
    public async Task CreateAsync(
        Guid userId,
        Guid familyId,
        NewRefreshToken token,
        SessionMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        context.RefreshTokens.Add(new RefreshToken
        {
            Id = token.Id,
            UserId = userId,
            FamilyId = familyId,
            TokenHash = token.TokenHash,
            ExpiresAt = token.ExpiresAt,
            UserAgent = metadata.UserAgent,
            IpAddress = metadata.IpAddress,
            CreatedAt = token.CreatedAt
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshTokenRotation> RotateAsync(
        string tokenHash,
        NewRefreshToken replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var token = await context.RefreshTokens
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.refresh_tokens
                WHERE token_hash = {tokenHash}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (token is null)
            return await RejectAsync(
                RefreshTokenRotation.Status.Missing, null, now,
                transaction, cancellationToken);

        if (token.ExpiresAt <= now || token.RevokedAt is not null)
            return await RejectAsync(
                RefreshTokenRotation.Status.Invalid, token.UserId, now,
                transaction, cancellationToken);

        if (token.UsedAt is not null || token.ReplacedByTokenId is not null)
        {
            await context.RefreshTokens
                .Where(candidate => candidate.FamilyId == token.FamilyId &&
                    candidate.RevokedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(candidate => candidate.RevokedAt, now),
                    cancellationToken);
            AddAudit("token.refresh.reuse", false, token.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenRotation(
                RefreshTokenRotation.Status.Reused, token.UserId);
        }

        context.RefreshTokens.Add(new RefreshToken
        {
            Id = replacement.Id,
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            TokenHash = replacement.TokenHash,
            ExpiresAt = replacement.ExpiresAt,
            DeviceName = token.DeviceName,
            UserAgent = token.UserAgent,
            IpAddress = token.IpAddress,
            CreatedAt = replacement.CreatedAt
        });
        await context.SaveChangesAsync(cancellationToken);
        token.UsedAt = now;
        token.ReplacedByTokenId = replacement.Id;
        AddAudit("token.refresh", true, token.UserId, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshTokenRotation(
            RefreshTokenRotation.Status.Succeeded, token.UserId);
    }

    public async Task RecordRejectedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        AddAudit("token.refresh", false, null, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<RefreshTokenRotation> RejectAsync(
        RefreshTokenRotation.Status status,
        Guid? userId,
        DateTimeOffset now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        AddAudit("token.refresh", false, userId, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshTokenRotation(status, userId);
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
