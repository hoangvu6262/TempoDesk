using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class PersonalWorkspaceProvisioner(
    TempoDeskIdentityDbContext context)
{
    private const string PersonalWorkspaceName = "Personal Workspace";

    public async Task EnsureAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH target_workspace AS (
                INSERT INTO public.workspaces (
                    id, name, type, owner_user_id, created_at, updated_at
                )
                VALUES (
                    {workspaceId},
                    {PersonalWorkspaceName},
                    {WorkspaceTypeValues.Personal},
                    {userId},
                    {now},
                    {now}
                )
                ON CONFLICT (owner_user_id) WHERE type = 'personal'
                DO UPDATE SET owner_user_id = EXCLUDED.owner_user_id
                RETURNING id
            )
            INSERT INTO public.workspace_members (
                id, workspace_id, user_id, role, created_at
            )
            SELECT
                {membershipId},
                target_workspace.id,
                {userId},
                {WorkspaceRoles.Owner},
                {now}
            FROM target_workspace
            ON CONFLICT (workspace_id, user_id) WHERE removed_at IS NULL
            DO NOTHING
            """,
            cancellationToken);
    }
}
