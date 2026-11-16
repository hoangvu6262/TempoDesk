using TempoDesk.Application.Workspaces;

namespace TempoDesk.Tests.Workspaces;

public sealed class WorkspaceMembershipPolicyTests
{
    [Theory]
    [InlineData(WorkspaceRoles.Admin, WorkspaceAction.InviteAdmin)]
    [InlineData(WorkspaceRoles.Member, WorkspaceAction.InviteMemberOrViewer)]
    [InlineData(WorkspaceRoles.Viewer, WorkspaceAction.InviteMemberOrViewer)]
    public void InvitationActionUsesRequestedRole(
        string role,
        WorkspaceAction expected)
    {
        Assert.Equal(expected,
            WorkspaceMembershipPolicy.InvitationAction(role));
    }

    [Theory]
    [InlineData(WorkspaceRoles.Admin, WorkspaceRoles.Member,
        WorkspaceAction.ChangeAdmin)]
    [InlineData(WorkspaceRoles.Member, WorkspaceRoles.Admin,
        WorkspaceAction.InviteAdmin)]
    [InlineData(WorkspaceRoles.Member, WorkspaceRoles.Viewer,
        WorkspaceAction.ChangeMemberOrViewerRole)]
    [InlineData(WorkspaceRoles.Viewer, WorkspaceRoles.Member,
        WorkspaceAction.ChangeMemberOrViewerRole)]
    public void RoleChangeActionUsesCurrentAndRequestedRoles(
        string currentRole,
        string requestedRole,
        WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceMembershipPolicy.RoleChangeAction(
            currentRole, requestedRole));
    }

    [Theory]
    [InlineData(WorkspaceRoles.Admin, WorkspaceAction.RemoveAdmin)]
    [InlineData(WorkspaceRoles.Member, WorkspaceAction.RemoveMemberOrViewer)]
    [InlineData(WorkspaceRoles.Viewer, WorkspaceAction.RemoveMemberOrViewer)]
    public void RemovalActionUsesPersistedRole(
        string role,
        WorkspaceAction expected)
    {
        Assert.Equal(expected, WorkspaceMembershipPolicy.RemovalAction(role));
    }

    [Fact]
    public void OwnerAndUnknownRolesFailClosed()
    {
        Assert.Null(WorkspaceMembershipPolicy.InvitationAction(
            WorkspaceRoles.Owner));
        Assert.Null(WorkspaceMembershipPolicy.RoleChangeAction(
            WorkspaceRoles.Owner, WorkspaceRoles.Admin));
        Assert.Null(WorkspaceMembershipPolicy.RoleChangeAction(
            WorkspaceRoles.Member, WorkspaceRoles.Owner));
        Assert.Null(WorkspaceMembershipPolicy.RemovalAction(
            WorkspaceRoles.Owner));
        Assert.Null(WorkspaceMembershipPolicy.RemovalAction("unknown"));
    }

    [Fact]
    public void InvitationTemplateContainsOnlyTheRawAcceptanceLink()
    {
        var content = WorkspaceInvitationEmailTemplates.Invitation(
            new Uri("https://app.tempodesk.test/base/"),
            "Product Team",
            "raw+/token");

        Assert.Contains("Product Team", content.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "https://app.tempodesk.test/base/accept-invitation?token=raw%2B%2Ftoken",
            content.TextBody,
            StringComparison.Ordinal);
        Assert.Contains("raw%2B%2Ftoken", content.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("hash", content.TextBody,
            StringComparison.OrdinalIgnoreCase);
    }
}
