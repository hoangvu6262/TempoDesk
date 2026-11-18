using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class MyWorkServiceTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TimeZoneDeterminesLocalToday()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await SeedTaskAsync(
            scenario.PersonalWorkspaceId,
            scenario.OwnerUserId,
            "Due",
            new DateOnly(2026, 11, 17));

        await using var chinaContext = database.CreateContext();
        var china = await Service(chinaContext, Now).GetAsync(
            scenario.OwnerUserId, "Asia/Shanghai");
        await using var usContext = database.CreateContext();
        var us = await Service(usContext, Now).GetAsync(
            scenario.OwnerUserId, "America/Los_Angeles");

        Assert.Single(china.Value!.Today);
        Assert.Single(us.Value!.Upcoming);
    }

    [Fact]
    public async Task MidnightDstTransitionUsesFirstValidLocalInstant()
    {
        var now = new DateTimeOffset(
            2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var scenario = await WorkItemScenario.CreateAsync(database, now);
        await using var context = database.CreateContext();

        var result = await Service(context, now).GetAsync(
            scenario.OwnerUserId, "America/Santiago");

        Assert.Equal(WorkItemResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task PersonalIncludesAllAndTeamIncludesOnlyAssignedTasks()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await SeedTaskAsync(
            scenario.PersonalWorkspaceId,
            scenario.OwnerUserId,
            "Personal",
            null);
        await SeedTaskAsync(
            scenario.TeamWorkspaceId,
            scenario.MemberUserId,
            "Assigned",
            null,
            assigneeUserId: scenario.OwnerUserId);
        await SeedTaskAsync(
            scenario.TeamWorkspaceId,
            scenario.MemberUserId,
            "Not assigned",
            null);

        await using var context = database.CreateContext();
        var result = await Service(context, Now).GetAsync(
            scenario.OwnerUserId, "Asia/Ho_Chi_Minh");

        Assert.Equal(
            ["Assigned", "Personal"],
            result.Value!.Unscheduled.Select(task => task.Title).Order());
    }

    [Fact]
    public async Task SixGroupsAreExclusiveAndExcludeArchivedOrDeletedWork()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Overdue", new(2026, 11, 16));
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Today", new(2026, 11, 17));
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Upcoming", new(2026, 11, 20));
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Later", new(2026, 11, 30));
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Unscheduled", null);
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Done", null,
            status: WorkItemStatusValues.Done,
            completedAt: Now.AddDays(-1));
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Deleted", null,
            deletedAt: Now);
        await SeedTaskAsync(scenario.PersonalWorkspaceId,
            scenario.OwnerUserId, "Archived", null,
            archivedProject: true);

        await using var context = database.CreateContext();
        var result = await Service(context, Now).GetAsync(
            scenario.OwnerUserId, "Asia/Ho_Chi_Minh");
        var groups = new[]
        {
            result.Value!.RecentlyCompleted,
            result.Value.Overdue,
            result.Value.Today,
            result.Value.Upcoming,
            result.Value.Later,
            result.Value.Unscheduled
        };

        Assert.All(groups, group => Assert.Single(group));
        Assert.Equal(6, groups.SelectMany(group => group)
            .Select(task => task.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public async Task InvalidTimeZoneReturnsValidation(string? timeZone)
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await using var context = database.CreateContext();

        var result = await Service(context, Now).GetAsync(
            scenario.OwnerUserId, timeZone);

        Assert.Equal(WorkItemResultStatus.Invalid, result.Status);
        Assert.Contains("timeZone", result.Errors!.Keys);
    }

    private async Task<Guid> SeedTaskAsync(
        Guid workspaceId,
        Guid creatorUserId,
        string title,
        DateOnly? dueDate,
        Guid? assigneeUserId = null,
        string status = WorkItemStatusValues.Todo,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? deletedAt = null,
        bool archivedProject = false)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = title,
            CreatedAt = Now,
            UpdatedAt = Now,
            ArchivedAt = archivedProject ? Now : null
        };
        var task = new WorkTask
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ProjectId = project.Id,
            CreatedByUserId = creatorUserId,
            Title = title,
            DueDate = dueDate,
            Status = status,
            SortKey = 1000m,
            CreatedAt = Now,
            UpdatedAt = Now,
            DeletedAt = deletedAt
        };
        if (assigneeUserId is not null)
        {
            task.Assignees.Add(new TaskAssignee
            {
                WorkspaceId = workspaceId,
                TaskId = task.Id,
                UserId = assigneeUserId.Value,
                AssignedAt = Now
            });
        }
        if (completedAt is not null)
        {
            task.ActivityLogs.Add(new TaskActivityLog
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                TaskId = task.Id,
                ActorUserId = creatorUserId,
                EventType = TaskActivityTypes.Created,
                Details = JsonSerializer.SerializeToDocument(
                    new { status = WorkItemStatusValues.Done }),
                CreatedAt = completedAt.Value
            });
        }
        project.Tasks.Add(task);
        await using var context = database.CreateContext();
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return task.Id;
    }

    private static MyWorkService Service(
        TempoDeskIdentityDbContext context,
        DateTimeOffset now) => new(
        context,
        new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
