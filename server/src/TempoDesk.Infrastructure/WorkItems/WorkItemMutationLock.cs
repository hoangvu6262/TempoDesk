using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.WorkItems;

internal static class WorkItemMutationLock
{
    public static async Task<IDbContextTransaction> BeginAsync(
        TempoDeskIdentityDbContext context,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            await context.Workspaces.FromSqlInterpolated($"""
                SELECT * FROM public.workspaces
                WHERE id = {workspaceId}
                FOR UPDATE
                """)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
