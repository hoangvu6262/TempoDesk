namespace TempoDesk.Application.Workspaces;

public static class WorkspaceMembershipPolicy
{
    public static WorkspaceAction? InvitationAction(string role) =>
        role switch
        {
            WorkspaceRoles.Admin => WorkspaceAction.InviteAdmin,
            WorkspaceRoles.Member or WorkspaceRoles.Viewer =>
                WorkspaceAction.InviteMemberOrViewer,
            _ => null
        };

    public static WorkspaceAction? RoleChangeAction(
        string currentRole,
        string requestedRole)
    {
        if (!IsManagedRole(currentRole) || !IsManagedRole(requestedRole))
            return null;

        if (currentRole == WorkspaceRoles.Admin)
            return WorkspaceAction.ChangeAdmin;

        return requestedRole == WorkspaceRoles.Admin
            ? WorkspaceAction.InviteAdmin
            : WorkspaceAction.ChangeMemberOrViewerRole;
    }

    public static WorkspaceAction? RemovalAction(string role) =>
        role switch
        {
            WorkspaceRoles.Admin => WorkspaceAction.RemoveAdmin,
            WorkspaceRoles.Member or WorkspaceRoles.Viewer =>
                WorkspaceAction.RemoveMemberOrViewer,
            _ => null
        };

    public static bool IsManagedRole(string? role) => role is
        WorkspaceRoles.Admin or WorkspaceRoles.Member or WorkspaceRoles.Viewer;
}
