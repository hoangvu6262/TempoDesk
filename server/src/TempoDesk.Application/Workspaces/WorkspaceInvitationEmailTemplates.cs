using System.Net;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Application.Workspaces;

public static class WorkspaceInvitationEmailTemplates
{
    public static EmailContent Invitation(
        Uri frontendBaseUrl,
        string workspaceName,
        string token)
    {
        ArgumentNullException.ThrowIfNull(frontendBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var link = $"{frontendBaseUrl.AbsoluteUri.TrimEnd('/')}" +
            $"/accept-invitation?token={Uri.EscapeDataString(token)}";
        var encodedLink = WebUtility.HtmlEncode(link);
        var encodedWorkspace = WebUtility.HtmlEncode(workspaceName);

        return new EmailContent(
            $"Join {workspaceName} on TempoDesk",
            $"You were invited to join {workspaceName}.\n\n{link}\n\n" +
            "If you did not expect this invitation, ignore this email.",
            $"<p>You were invited to join {encodedWorkspace}.</p>" +
            $"<p><a href=\"{encodedLink}\">Accept invitation</a></p>" +
            "<p>If you did not expect this invitation, ignore this email.</p>");
    }
}
