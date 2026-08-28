using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// Covers the Search, Priority, DueDateFrom and DueDateTo filters added in SEA-04.
/// </summary>
public class TaskSearchFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 10, 0, 0, TimeSpan.Zero);

    // ── Search (title substring) ──────────────────────────────────────────

    [Fact]
    public async Task GetMyTasks_Search_Filters_By_Title()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();

        ctx.TaskItems.AddRange(
            MakeUserTask(companyId, userId, "Complete onboarding paperwork", TaskPriority.Medium, null),
            MakeUserTask(companyId, userId, "Review benefits package", TaskPriority.Medium, null));
        await ctx.SaveChangesAsync();

        var result = await GetMyTasksHandler(ctx).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId, Search = "onboarding" },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Contains("onboarding", result.Items[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    // ── Priority filter ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTasks_Priority_Filters_Tasks()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();

        ctx.TaskItems.AddRange(
            MakeUserTask(companyId, userId, "High priority task",   TaskPriority.High,   null),
            MakeUserTask(companyId, userId, "Medium priority task", TaskPriority.Medium, null),
            MakeUserTask(companyId, userId, "Low priority task",    TaskPriority.Low,    null));
        await ctx.SaveChangesAsync();

        var result = await GetMyTasksHandler(ctx).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId, Priority = "High" },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("High priority task", result.Items[0].Title);
    }

    // ── DueDateFrom / DueDateTo ───────────────────────────────────────────

    [Fact]
    public async Task GetEmployeeTasks_DueDateFrom_Excludes_Earlier_Tasks()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueEarly   = new DateOnly(2026, 1, 1);
        var dueLate    = new DateOnly(2026, 12, 1);

        ctx.TaskItems.AddRange(
            MakeEmployeeTask(companyId, employeeId, "Old task", TaskPriority.Medium, dueEarly),
            MakeEmployeeTask(companyId, employeeId, "New task", TaskPriority.Medium, dueLate));
        await ctx.SaveChangesAsync();

        var result = await GetEmployeeTasksHandler(ctx).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId, DueDateFrom = new DateOnly(2026, 6, 1) },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("New task", result.Items[0].Title);
    }

    [Fact]
    public async Task GetEmployeeTasks_DueDateTo_Excludes_Later_Tasks()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueEarly   = new DateOnly(2026, 1, 1);
        var dueLate    = new DateOnly(2026, 12, 1);

        ctx.TaskItems.AddRange(
            MakeEmployeeTask(companyId, employeeId, "Old task", TaskPriority.Medium, dueEarly),
            MakeEmployeeTask(companyId, employeeId, "New task", TaskPriority.Medium, dueLate));
        await ctx.SaveChangesAsync();

        var result = await GetEmployeeTasksHandler(ctx).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId, DueDateTo = new DateOnly(2026, 6, 1) },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Old task", result.Items[0].Title);
    }

    [Fact]
    public async Task GetEmployeeTasks_Tasks_Without_DueDate_Excluded_When_DueDateFrom_Set()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.TaskItems.AddRange(
            MakeEmployeeTask(companyId, employeeId, "No due date", TaskPriority.Medium, null),
            MakeEmployeeTask(companyId, employeeId, "Has due date", TaskPriority.Medium, new DateOnly(2026, 12, 1)));
        await ctx.SaveChangesAsync();

        var result = await GetEmployeeTasksHandler(ctx).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId, DueDateFrom = new DateOnly(2026, 1, 1) },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Has due date", result.Items[0].Title);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static GetMyTasksHandler GetMyTasksHandler(TasksDbContext ctx) =>
        new(ctx, new FakeEmployeeNameReader());

    private static GetEmployeeTasksHandler GetEmployeeTasksHandler(TasksDbContext ctx) =>
        new(ctx, new FakeEmployeeNameReader());

    private static TaskItem MakeUserTask(Guid companyId, Guid userId, string title, TaskPriority priority, DateOnly? dueDate) =>
        TaskItem.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), title, null, priority,
            TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId: userId, assignedUserId: userId, Now);

    private static TaskItem MakeEmployeeTask(Guid companyId, Guid employeeId, string title, TaskPriority priority, DateOnly? dueDate) =>
        TaskItem.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), title, null, priority,
            TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId: employeeId, assignedUserId: null, Now);

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
