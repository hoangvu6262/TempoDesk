using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AccountLoginService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    TempoDeskIdentityDbContext context,
    IOptions<AuthenticationOptions> configured,
    TimeProvider timeProvider) : IAccountLoginService
{
    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
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

        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Iat,
                    EpochTime.GetIntDate(now.UtcDateTime).ToString(
                        CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64)
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        AddAudit(true, user.Id, now);
        await context.SaveChangesAsync(cancellationToken);
        return new LoginResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            checked(settings.AccessTokenMinutes * 60));
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
