namespace TempoDesk.Api.Controllers;

public sealed record LoginResponseDto(
    string AccessToken,
    string TokenType,
    int ExpiresIn);
