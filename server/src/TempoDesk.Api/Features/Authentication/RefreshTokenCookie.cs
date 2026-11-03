namespace TempoDesk.Api.Controllers;

internal static class RefreshTokenCookie
{
    private const string Name = "tempodesk.refresh_token";

    public static string Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var token) ? token : "";

    public static void Write(
        HttpResponse response,
        bool secure,
        string token,
        DateTimeOffset expiresAt) => response.Cookies.Append(
        Name,
        token,
        Options(secure, expiresAt));

    public static void Delete(HttpResponse response, bool secure) =>
        response.Cookies.Delete(Name, Options(secure));

    private static CookieOptions Options(
        bool secure,
        DateTimeOffset? expiresAt = null) => new()
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = expiresAt
        };
}
