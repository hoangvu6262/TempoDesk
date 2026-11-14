using System.Security.Cryptography;
using System.Text;

namespace TempoDesk.Application.Authentication;

public static class AuthenticationRateLimitKeys
{
    public static string ForAccount(string email) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            email.Trim().ToUpperInvariant()))).ToLowerInvariant();
}
