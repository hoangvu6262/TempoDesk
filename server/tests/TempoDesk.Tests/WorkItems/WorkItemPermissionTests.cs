using TempoDesk.Application.Workspaces;

namespace TempoDesk.Tests.WorkItems;

public sealed class WorkItemPermissionTests
{
    [Theory]
    [InlineData(WorkspaceRoles.Owner, WorkspaceAction.ManageProjects, true)]
    [InlineData(WorkspaceRoles.Admin, WorkspaceAction.ManageProjects, true)]
    [InlineData(WorkspaceRoles.Member, WorkspaceAction.ManageProjects, false)]
    [InlineData(WorkspaceRoles.Viewer, WorkspaceAction.ManageProjects, false)]
    [InlineData(WorkspaceRoles.Owner, WorkspaceAction.ManageTasks, true)]
    [InlineData(WorkspaceRoles.Admin, WorkspaceAction.ManageTasks, true)]
    [InlineData(WorkspaceRoles.Member, WorkspaceAction.ManageTasks, true)]
    [InlineData(WorkspaceRoles.Viewer, WorkspaceAction.ManageTasks, false)]
    [InlineData(WorkspaceRoles.Owner, WorkspaceAction.ViewWorkItems, true)]
    [InlineData(WorkspaceRoles.Admin, WorkspaceAction.ViewWorkItems, true)]
    [InlineData(WorkspaceRoles.Member, WorkspaceAction.ViewWorkItems, true)]
    [InlineData(WorkspaceRoles.Viewer, WorkspaceAction.ViewWorkItems, true)]
    public void WorkItemActionsFollowMatrix(
        string role,
        WorkspaceAction action,
        bool expected) =>
        Assert.Equal(expected, WorkspacePermissionPolicy.Allows(role, action));
}
