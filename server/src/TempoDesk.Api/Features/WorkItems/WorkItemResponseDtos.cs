namespace TempoDesk.Api.Controllers;

public sealed record ProjectResponseDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
