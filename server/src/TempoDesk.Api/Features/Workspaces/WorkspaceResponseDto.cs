namespace TempoDesk.Api.Controllers;

public sealed record WorkspaceResponseDto(
    Guid Id,
    string Name,
    string Type,
    string Role);
