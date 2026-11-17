using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/my-work")]
public sealed class MyWorkController(
    IMyWorkService myWork) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken)
    {
        var result = await myWork.GetAsync(
            WorkItemControllerSupport.UserId(this),
            timeZone,
            cancellationToken);
        if (result.Status != WorkItemResultStatus.Success)
            return WorkItemControllerSupport.Failure(this, result);

        var work = result.Value!;
        return Ok(new MyWorkResponseDto(
            Map(work.RecentlyCompleted),
            Map(work.Overdue),
            Map(work.Today),
            Map(work.Upcoming),
            Map(work.Later),
            Map(work.Unscheduled)));
    }

    private static List<TaskResponseDto> Map(
        IEnumerable<TaskSummary> tasks) => tasks
        .Select(WorkItemControllerSupport.ToDto)
        .ToList();
}
