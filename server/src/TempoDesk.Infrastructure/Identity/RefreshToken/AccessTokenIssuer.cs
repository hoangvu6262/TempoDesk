using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AccessTokenIssuer(
    IOptions<AuthenticationOptions> configured,
    TimeProvider timeProvider)
{
    public (string Value, int ExpiresIn) Issue(Guid userId)
    {
        var settings = configured.Value;
        var now = timeProvider.GetUtcNow();
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Iat,
                    EpochTime.GetIntDate(now.UtcDateTime).ToString(
                        CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64)
            ],
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(settings.AccessTokenMinutes).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return (
            new JwtSecurityTokenHandler().WriteToken(token),
            checked(settings.AccessTokenMinutes * 60));
    }
}
