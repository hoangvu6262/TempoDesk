namespace TempoDesk.Application.Workspaces;

public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    string Type,
    string Role);
