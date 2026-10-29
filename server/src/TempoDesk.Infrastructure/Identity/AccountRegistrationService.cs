using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AccountRegistrationService(
    UserManager<ApplicationUser> users,
    TempoDeskIdentityDbContext context,
    IEmailVerificationSender sender,
    TimeProvider timeProvider) : IAccountRegistrationService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<RegistrationResult> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!existing.EmailConfirmed)
                await ResendVerificationAsync(email, cancellationToken);

            return RegistrationResult.Accepted;
        }

        var now = timeProvider.GetUtcNow();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var duplicate = await users.FindByEmailAsync(email);
            if (duplicate is not null && !duplicate.EmailConfirmed)
                await ResendVerificationAsync(email, cancellationToken);

            return RegistrationResult.Accepted;
        }
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IsDuplicate(created)
                ? RegistrationResult.Accepted
                : new RegistrationResult(created.Errors
                    .GroupBy(error => error.Code, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray(),
                        StringComparer.Ordinal));
        }

        var message = AddToken(user, now);
        AddAudit("user.registration", true, user.Id, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await DeliverAsync(message, user.Id, cancellationToken);

        return RegistrationResult.Accepted;
    }

    public async Task<bool> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            AddAudit("email.verification", false, null, now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        var tokenHash = HashToken(token);
        var candidate = await context.EmailVerificationTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash,
                cancellationToken);
        if (candidate is null || candidate.UsedAt is not null ||
            candidate.ExpiresAt <= now)
        {
            AddAudit("email.verification", false, candidate?.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var lockedUser = await LockUserAsync(candidate.UserId, cancellationToken);
        now = timeProvider.GetUtcNow();
        if (lockedUser is null || lockedUser.EmailConfirmed)
        {
            AddAudit("email.verification", false, candidate.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var claimed = await context.EmailVerificationTokens
            .Where(item => item.Id == candidate.Id && item.UsedAt == null &&
                item.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.UsedAt, now), cancellationToken);
        if (claimed != 1)
        {
            AddAudit("email.verification", false, candidate.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var user = await users.FindByIdAsync(candidate.UserId.ToString());
        if (user is null)
        {
            AddAudit("email.verification", false, candidate.UserId, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        user.EmailConfirmed = true;
        user.UpdatedAt = now;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        AddAudit("email.verification", true, user.Id, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task ResendVerificationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed)
            return;

        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var lockedUser = await LockUserAsync(user.Id, cancellationToken);
        if (lockedUser is null || lockedUser.EmailConfirmed)
            return;

        await context.EmailVerificationTokens
            .Where(token => token.UserId == lockedUser.Id && token.UsedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(token => token.UsedAt, now), cancellationToken);
        var message = AddToken(lockedUser, now);
        AddAudit("email.verification.resent", true, lockedUser.Id, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await DeliverAsync(message, lockedUser.Id, cancellationToken);
    }

    private Task<ApplicationUser?> LockUserAsync(
        Guid userId,
        CancellationToken cancellationToken) => context.Users
        .FromSqlInterpolated(
            $"SELECT * FROM public.users WHERE id = {userId} FOR UPDATE")
        .AsNoTracking()
        .SingleOrDefaultAsync(cancellationToken);

    private VerificationEmail AddToken(
        ApplicationUser user,
        DateTimeOffset now)
    {
        var token = CreateToken();
        var expiresAt = now.Add(TokenLifetime);
        context.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(token),
            ExpiresAt = expiresAt,
            CreatedAt = now
        });
        AddAudit("email.verification.issued", true, user.Id, now);
        return new VerificationEmail(user.Email!, token, expiresAt);
    }

    private async Task DeliverAsync(
        VerificationEmail message,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var delivered = false;
        try
        {
            delivered = await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Delivery is deferred; registration must not reveal provider failures.
        }

        if (delivered)
            return;

        AddAudit("email.verification.delivery", false, userId,
            timeProvider.GetUtcNow());
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

    private static bool IsDuplicate(IdentityResult result) => result.Errors.Any(error =>
        error.Code is "DuplicateEmail" or "DuplicateUserName");

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
