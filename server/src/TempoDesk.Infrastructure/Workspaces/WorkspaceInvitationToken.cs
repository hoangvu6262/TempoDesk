using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace TempoDesk.Infrastructure.Workspaces;

internal static class WorkspaceInvitationToken
{
    private const int ByteLength = 32;
    private const int EncodedLength = 43;

    public static string Create() => WebEncoders.Base64UrlEncode(
        RandomNumberGenerator.GetBytes(ByteLength));

    public static bool TryHash(string? token, out string hash)
    {
        hash = "";
        if (token is null || token.Length != EncodedLength)
            return false;

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            if (bytes.Length != ByteLength ||
                WebEncoders.Base64UrlEncode(bytes) != token)
            {
                return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        return true;
    }
}
