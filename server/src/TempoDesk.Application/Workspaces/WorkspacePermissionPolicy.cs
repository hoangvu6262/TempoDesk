namespace TempoDesk.Application.Workspaces;

public static class WorkspacePermissionPolicy
{
    public static bool Allows(string role, WorkspaceAction action) =>
        action switch
        {
            WorkspaceAction.ViewWorkspace or
            WorkspaceAction.ViewMembers or
            WorkspaceAction.ViewWorkItems => role is
                WorkspaceRoles.Owner or
                WorkspaceRoles.Admin or
                WorkspaceRoles.Member or
                WorkspaceRoles.Viewer,

            WorkspaceAction.RenameWorkspace or
            WorkspaceAction.ManageProjects or
            WorkspaceAction.InviteMemberOrViewer or
            WorkspaceAction.ChangeMemberOrViewerRole or
            WorkspaceAction.RemoveMemberOrViewer => role is
                WorkspaceRoles.Owner or WorkspaceRoles.Admin,

            WorkspaceAction.ManageTasks => role is
                WorkspaceRoles.Owner or
                WorkspaceRoles.Admin or
                WorkspaceRoles.Member,

            WorkspaceAction.InviteAdmin or
            WorkspaceAction.ChangeAdmin or
            WorkspaceAction.RemoveAdmin or
            WorkspaceAction.TransferOwnership =>
                role == WorkspaceRoles.Owner,

            WorkspaceAction.LeaveWorkspace => role is
                WorkspaceRoles.Admin or
                WorkspaceRoles.Member or
                WorkspaceRoles.Viewer,

            _ => false
        };
}
