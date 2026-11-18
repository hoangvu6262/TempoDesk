namespace TempoDesk.Application.WorkItems;

public interface IMyWorkService
{
    Task<WorkItemResult<MyWorkSummary>> GetAsync(
        Guid actorUserId,
        string? timeZone,
        CancellationToken cancellationToken = default);
}
