using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class WorkspaceAuthorizationService(
    TempoDeskIdentityDbContext context) : IWorkspaceAuthorizationService
{
    public async Task<WorkspaceAuthorizationResult> AuthorizeAsync(
        Guid userId,
        Guid workspaceId,
        WorkspaceAction action,
        CancellationToken cancellationToken = default)
    {
        var role = await context.WorkspaceMembers
            .AsNoTracking()
            .Where(member => member.UserId == userId &&
                member.WorkspaceId == workspaceId &&
                member.RemovedAt == null)
            .Select(member => member.Role)
            .SingleOrDefaultAsync(cancellationToken);

        if (role is null)
            return WorkspaceAuthorizationResult.Missing;

        return WorkspacePermissionPolicy.Allows(role, action)
            ? WorkspaceAuthorizationResult.Granted(role)
            : WorkspaceAuthorizationResult.Denied;
    }
}
