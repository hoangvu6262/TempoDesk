namespace TempoDesk.Api.Controllers;

public sealed record CreateWorkspaceRequestDto(string? Name);

public sealed record RenameWorkspaceRequestDto(string? Name);
