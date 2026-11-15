namespace TempoDesk.Application.Workspaces;

public enum WorkspaceResultStatus
{
    Success,
    InvalidName,
    NotFound,
    Forbidden,
    PersonalNameManaged
}

public sealed record WorkspaceResult(
    WorkspaceResultStatus Status,
    WorkspaceSummary? Workspace = null,
    string? ValidationError = null)
{
    public static WorkspaceResult Succeeded(WorkspaceSummary workspace) =>
        new(WorkspaceResultStatus.Success, workspace);

    public static WorkspaceResult Invalid(string error) =>
        new(WorkspaceResultStatus.InvalidName, ValidationError: error);

    public static WorkspaceResult Missing { get; } =
        new(WorkspaceResultStatus.NotFound);

    public static WorkspaceResult Denied { get; } =
        new(WorkspaceResultStatus.Forbidden);

    public static WorkspaceResult PersonalNameConflict { get; } =
        new(WorkspaceResultStatus.PersonalNameManaged);
}
