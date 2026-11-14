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

    public async Task LogoutAsync(
        string? tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var token = tokenHash is null
            ? null
            : await context.RefreshTokens
                .AsNoTracking()
                .Where(candidate => candidate.TokenHash == tokenHash)
                .Select(candidate => new
                {
                    candidate.UserId,
                    candidate.FamilyId
                })
                .SingleOrDefaultAsync(cancellationToken);
        var changed = token is null
            ? 0
            : await context.RefreshTokens
                .Where(candidate => candidate.FamilyId == token.FamilyId &&
                    candidate.RevokedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(candidate => candidate.RevokedAt, now),
                    cancellationToken);
        AddAudit("auth.logout", changed > 0, token?.UserId, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuthenticationSession>> ListSessionsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .Select(token => new
            {
                token.FamilyId,
                token.UserAgent,
                token.IpAddress,
                token.CreatedAt,
                token.UsedAt,
                token.ExpiresAt,
                token.RevokedAt
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.FamilyId)
            .Select(family =>
            {
                var createdAt = family.Min(row => row.CreatedAt);
                var active = family.Any(row =>
                    row.RevokedAt is null && row.ExpiresAt > now);
                var allRevoked = family.All(row =>
                    row.RevokedAt is not null);
                return new AuthenticationSession(
                    family.Key,
                    family.Select(row => row.UserAgent)
                        .FirstOrDefault(value => value is not null),
                    family.Select(row => row.IpAddress?.ToString())
                        .FirstOrDefault(value => value is not null),
                    createdAt,
                    family.Where(row => row.UsedAt is not null)
                        .Select(row => row.UsedAt!.Value)
                        .DefaultIfEmpty(createdAt)
                        .Max(),
                    family.Max(row => row.ExpiresAt),
                    active
                        ? AuthenticationSessionStatus.Active
                        : allRevoked
                            ? AuthenticationSessionStatus.Revoked
                            : AuthenticationSessionStatus.Expired);
            })
            .OrderByDescending(session => session.LastUsedAt)
            .ToArray();
    }

    public async Task<bool> RevokeSessionAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var owned = await context.RefreshTokens
            .AnyAsync(token => token.UserId == userId &&
                token.FamilyId == familyId, cancellationToken);
        if (owned)
            await context.RefreshTokens
                .Where(token => token.UserId == userId &&
                    token.FamilyId == familyId && token.RevokedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(token => token.RevokedAt, now),
                    cancellationToken);
        AddAudit("session.revoke", owned, userId, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return owned;
    }

    public async Task RevokeAllSessionsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await context.RefreshTokens
            .Where(token => token.UserId == userId &&
                token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(token => token.RevokedAt, now),
                cancellationToken);
        AddAudit("session.revoke_all", true, userId, now);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }
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
