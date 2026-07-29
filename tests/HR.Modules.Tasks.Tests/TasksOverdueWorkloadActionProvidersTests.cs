using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// OBT-721 workload action provider tests for the two Tasks providers: the self-scoped
/// EmployeeTasksOverdueWorkloadActionProvider (caller only ever sees their own overdue tasks) and
/// the ManagerTasksOverdueWorkloadActionProvider (Manager scoped to direct reports, HR company-wide).
/// </summary>
public class TasksOverdueWorkloadActionProvidersTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TasksDbContext(options);
    }

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static TaskItem CreateOverdueTask(Guid companyId, Guid? assignedEmployeeId, string title, DateOnly dueDate) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), title, null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId, null, DateTimeOffset.UtcNow);

    // ── EmployeeTasksOverdueWorkloadActionProvider (self-scoped) ────────────────

    [Fact]
    public async Task EmployeeProvider_Returns_Only_Callers_Own_Overdue_Tasks()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        context.TaskItems.AddRange(
            CreateOverdueTask(companyId, callerId, "My overdue task", Today.AddDays(-1)),
            CreateOverdueTask(companyId, otherEmployeeId, "Someone else's overdue task", Today.AddDays(-1)));
        await context.SaveChangesAsync();

        var provider = new EmployeeTasksOverdueWorkloadActionProvider(context, new FakeEmployeeDepartmentReader());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("My overdue task", action.ActionType);
        Assert.Equal(callerId, action.EmployeeId);
    }

    [Fact]
    public async Task EmployeeProvider_CallerWithNoSubClaim_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.TaskItems.Add(CreateOverdueTask(companyId, Guid.NewGuid(), "Some task", Today.AddDays(-1)));
        await context.SaveChangesAsync();

        var provider = new EmployeeTasksOverdueWorkloadActionProvider(context, new FakeEmployeeDepartmentReader());

        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EmployeeProvider_Maps_ActionCategory_DueDate_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var dueDate = Today.AddDays(-3);

        var task = CreateOverdueTask(companyId, callerId, "Finish onboarding checklist", dueDate);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var provider = new EmployeeTasksOverdueWorkloadActionProvider(context, new FakeEmployeeDepartmentReader());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Employee Tasks Overdue", action.ActionCategory);
        Assert.Equal(dueDate, action.DueDate);
        Assert.Equal($"/companies/{companyId}/tasks/{task.Id}", action.DeepLinkUrl);
        Assert.Equal("Overdue", action.Status);
    }

    // ── ManagerTasksOverdueWorkloadActionProvider ───────────────────────────────

    [Fact]
    public async Task ManagerProvider_HrCaller_Sees_All_Overdue_Tasks_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.TaskItems.AddRange(
            CreateOverdueTask(companyId, Guid.NewGuid(), "Task A", Today.AddDays(-1)),
            CreateOverdueTask(companyId, Guid.NewGuid(), "Task B", Today.AddDays(-2)));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ManagerProvider_ManagerCaller_Is_Scoped_To_DirectReports_Only()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        context.TaskItems.AddRange(
            CreateOverdueTask(companyId, directReportId, "Team task", Today.AddDays(-1)),
            CreateOverdueTask(companyId, otherEmployeeId, "Other team's task", Today.AddDays(-1)));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
    }

    [Fact]
    public async Task ManagerProvider_ManagerWithNoDirectReports_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.TaskItems.Add(CreateOverdueTask(companyId, Guid.NewGuid(), "Task", Today.AddDays(-1)));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ManagerProvider_CallerWithNoSubClaim_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.TaskItems.Add(CreateOverdueTask(companyId, Guid.NewGuid(), "Task", Today.AddDays(-1)));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }
}
