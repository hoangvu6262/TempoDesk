using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AccountLoginService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    TempoDeskIdentityDbContext context,
    IAuthenticationTokenService tokens,
    IOptions<AuthenticationOptions> configured,
    TimeProvider timeProvider) : IAccountLoginService
{
    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        SessionMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email);
        var settings = configured.Value;
        if (user is null || !settings.Enabled)
            return await RejectAsync(user?.Id, cancellationToken);

        var result = await signIn.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return await RejectAsync(user.Id, cancellationToken);

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var issued = await tokens.IssueAsync(
            user.Id, metadata ?? SessionMetadata.Empty, cancellationToken);
        AddAudit(true, user.Id, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LoginResult(
            issued.AccessToken,
            issued.ExpiresIn,
            issued.RefreshToken,
            issued.RefreshTokenExpiresAt);
    }

    private async Task<LoginResult> RejectAsync(
        Guid? userId,
        CancellationToken cancellationToken)
    {
        AddAudit(false, userId, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return LoginResult.Rejected;
    }

    private void AddAudit(
        bool succeeded,
        Guid? userId,
        DateTimeOffset occurredAt) => context.SecurityAuditLogs.Add(
        new SecurityAuditLog
        {
            UserId = userId,
            EventType = "user.login",
            Succeeded = succeeded,
            OccurredAt = occurredAt
        });
}
