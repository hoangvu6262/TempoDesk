namespace TempoDesk.Api.Controllers;

public sealed record AccessTokenResponseDto(
    string AccessToken,
    string TokenType,
    int ExpiresIn);
