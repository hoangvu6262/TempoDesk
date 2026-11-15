namespace TempoDesk.Application.Workspaces;

public enum WorkspaceAuthorizationStatus
{
    Allowed,
    NotFound,
    Forbidden
}

public sealed record WorkspaceAuthorizationResult(
    WorkspaceAuthorizationStatus Status,
    string? Role = null)
{
    public static WorkspaceAuthorizationResult Granted(string role) =>
        new(WorkspaceAuthorizationStatus.Allowed, role);

    public static WorkspaceAuthorizationResult Missing { get; } =
        new(WorkspaceAuthorizationStatus.NotFound);

    public static WorkspaceAuthorizationResult Denied { get; } =
        new(WorkspaceAuthorizationStatus.Forbidden);
}
