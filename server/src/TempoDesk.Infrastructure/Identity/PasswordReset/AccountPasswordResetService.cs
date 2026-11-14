using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AccountPasswordResetService(
    UserManager<ApplicationUser> users,
    TempoDeskIdentityDbContext context,
    IPasswordResetEmailSender sender,
    IAuthenticationSessionService sessions,
    TimeProvider timeProvider) : IAccountPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public async Task ForgotPasswordAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email);
        var now = timeProvider.GetUtcNow();
        if (user is null)
        {
            AddAudit("password.reset.requested", false, null, now);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var rawToken = CreateToken();
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var locked = await context.Users
            .FromSqlInterpolated($"""
                SELECT * FROM public.users WHERE id = {user.Id} FOR UPDATE
                """)
            .SingleAsync(cancellationToken);
        await context.PasswordResetTokens
            .Where(token => token.UserId == locked.Id && token.UsedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(token => token.UsedAt, now), cancellationToken);
        context.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = locked.Id,
            TokenHash = Hash(rawToken),
            ExpiresAt = now.Add(TokenLifetime),
            CreatedAt = now
        });
        AddAudit("password.reset.requested", true, locked.Id, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await sender.SendAsync(
            new PasswordResetEmail(locked.Email!, rawToken), cancellationToken);
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
            return await RejectTokenAsync(null, now, cancellationToken);

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var candidate = await context.PasswordResetTokens
            .FromSqlInterpolated($"""
                SELECT * FROM public.password_reset_tokens
                WHERE token_hash = {Hash(token)} FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (candidate is null || candidate.UsedAt is not null ||
            candidate.ExpiresAt <= now)
        {
            AddAudit("password.reset", false, candidate?.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResetPasswordResult.InvalidToken;
        }

        var user = await users.FindByIdAsync(candidate.UserId.ToString());
        if (user is null)
        {
            AddAudit("password.reset", false, candidate.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResetPasswordResult.InvalidToken;
        }

        candidate.UsedAt = now;
        var removed = await users.RemovePasswordAsync(user);
        var added = removed.Succeeded
            ? await users.AddPasswordAsync(user, newPassword)
            : removed;
        if (!added.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            AddAudit("password.reset", false, user.Id, now);
            await context.SaveChangesAsync(cancellationToken);
            return new ResetPasswordResult(
                false,
                false,
                added.Errors
                    .GroupBy(error => error.Code, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description)
                            .ToArray(),
                        StringComparer.Ordinal));
        }

        await sessions.RevokeAllAsync(user.Id, cancellationToken);
        AddAudit("password.reset", true, user.Id, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ResetPasswordResult.Success;
    }

    private async Task<ResetPasswordResult> RejectTokenAsync(
        Guid? userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AddAudit("password.reset", false, userId, now);
        await context.SaveChangesAsync(cancellationToken);
        return ResetPasswordResult.InvalidToken;
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

    private static string CreateToken() => Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
