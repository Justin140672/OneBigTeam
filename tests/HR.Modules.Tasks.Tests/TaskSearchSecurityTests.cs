// SEA-08: Search security matrix — task search cross-company isolation,
// consistent out-of-range page behaviour and validator coverage.
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Features.GetTeamTasks;
using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class TaskSearchSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    // ── Cross-company isolation — GetMyTasks ───────────────────────────────

    [Fact]
    public async Task GetMyTasks_TotalCount_Excludes_Other_Company_Tasks()
    {
        await using var ctx = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        ctx.TaskItems.AddRange(
            MakeTask(companyA, userId),
            MakeTask(companyA, userId),
            MakeTask(companyB, userId));   // same user, different company
        await ctx.SaveChangesAsync();

        var result = await new GetMyTasksHandler(ctx, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyA, UserId = userId },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetMyTasks_Out_Of_Range_Page_Returns_Empty_Items_With_Correct_TotalCount()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();

        ctx.TaskItems.Add(MakeTask(companyId, userId));
        await ctx.SaveChangesAsync();

        var result = await new GetMyTasksHandler(ctx, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId, PageNumber = 999, PageSize = 20 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Empty(result.Items);
    }

    // ── Cross-company isolation — GetTeamTasks ────────────────────────────

    [Fact]
    public async Task GetTeamTasks_TotalCount_Excludes_Other_Company_Tasks()
    {
        await using var ctx = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        ctx.TaskItems.AddRange(
            MakeTask(companyA, reportId),
            MakeTask(companyA, reportId),
            MakeTask(companyB, reportId));   // same report employee, different company
        await ctx.SaveChangesAsync();

        var result = await new GetTeamTasksHandler(
            ctx, new FakeDirectReportsReader(reportId), new FakeEmployeeNameReader()).HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyA, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    // ── Cross-company isolation — GetEmployeeTasks ────────────────────────

    [Fact]
    public async Task GetEmployeeTasks_TotalCount_Excludes_Other_Company_Tasks()
    {
        await using var ctx = BuildContext();
        var companyA    = Guid.NewGuid();
        var companyB    = Guid.NewGuid();
        var employeeId  = Guid.NewGuid();

        ctx.TaskItems.AddRange(
            MakeTaskForEmployee(companyA, employeeId),
            MakeTaskForEmployee(companyA, employeeId),
            MakeTaskForEmployee(companyB, employeeId));  // same employee, different company
        await ctx.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(ctx, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    // ── Validator coverage ─────────────────────────────────────────────────

    [Fact]
    public void GetMyTasksValidator_Rejects_Zero_PageNumber()
    {
        var result = new GetMyTasksValidator().Validate(
            new GetMyTasksRequest { CompanyId = Guid.NewGuid(), PageNumber = 0 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetMyTasksValidator_Rejects_Oversized_PageSize()
    {
        var result = new GetMyTasksValidator().Validate(
            new GetMyTasksRequest { CompanyId = Guid.NewGuid(), PageSize = 201 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetTeamTasksValidator_Rejects_Zero_PageNumber()
    {
        var result = new GetTeamTasksValidator().Validate(
            new GetTeamTasksRequest { CompanyId = Guid.NewGuid(), ManagerId = Guid.NewGuid(), PageNumber = 0 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetEmployeeTasksValidator_Rejects_Zero_PageNumber()
    {
        var result = new GetEmployeeTasksValidator().Validate(
            new GetEmployeeTasksRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), PageNumber = 0 });
        Assert.False(result.IsValid);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static TaskItem MakeTask(Guid companyId, Guid assignedEmployeeId) =>
        TaskItem.Create(Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId: assignedEmployeeId, assignedUserId: null, Now);

    private static TaskItem MakeTaskForEmployee(Guid companyId, Guid assignedEmployeeId) =>
        TaskItem.Create(Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId, assignedUserId: null, Now);

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TasksDbContext(options);
    }
}
