using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class GetEmployeeTasksHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static TaskItem MakeTask(
        Guid companyId,
        Guid? assignedEmployeeId,
        string title = "Task",
        TaskItemStatus status = TaskItemStatus.Open,
        DateOnly? dueDate = null)
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, TaskSource.Manual, TaskActionType.Complete,
            dueDate, assignedEmployeeId, null, Now);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task HandleAsync_Returns_Tasks_Assigned_To_Employee()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherId    = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, employeeId, "Employee task A"),
            MakeTask(companyId, employeeId, "Employee task B"),
            MakeTask(companyId, otherId,    "Other employee task"),
            MakeTask(companyId, null,       "Unassigned task"));

        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(employeeId, item.AssignedEmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Employee_Has_No_Tasks()
    {
        await using var context = BuildContext();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Tasks_From_Other_Companies()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();
        var companyA   = Guid.NewGuid();
        var companyB   = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyA, employeeId, "Company A task"),
            MakeTask(companyB, employeeId, "Company B task"));

        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Company A task", result.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_When_Provided()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, employeeId, "Open task",      TaskItemStatus.Open),
            MakeTask(companyId, employeeId, "In progress",    TaskItemStatus.InProgress),
            MakeTask(companyId, employeeId, "Completed task", TaskItemStatus.Completed));

        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId, Status = "Completed" },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Completed", result.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Status_Filter_Is_Case_Insensitive()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, employeeId, "Open task",  TaskItemStatus.Open),
            MakeTask(companyId, employeeId, "Cancelled",  TaskItemStatus.Cancelled));

        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId, Status = "cancelled" },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Cancelled", result.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Unknown_Status_And_Returns_All()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, employeeId, "Task A"),
            MakeTask(companyId, employeeId, "Task B"));

        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId, Status = "bogus" },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_DueDate_Then_CreatedAt_Nulls_Last()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, employeeId, "No due date", dueDate: null),
            MakeTask(companyId, employeeId, "Due later",   dueDate: new DateOnly(2026, 9, 1)),
            MakeTask(companyId, employeeId, "Due soon",    dueDate: new DateOnly(2026, 7, 1)));

        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal("Due soon",    result.Items[0].Title);
        Assert.Equal("Due later",   result.Items[1].Title);
        Assert.Equal("No due date", result.Items[2].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Fields_Correctly()
    {
        await using var context = BuildContext();
        var companyId    = Guid.NewGuid();
        var employeeId   = Guid.NewGuid();
        var assignedUser = Guid.NewGuid();
        var createdBy    = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            "Full task", "Details here",
            TaskPriority.High, TaskSource.Onboarding, TaskActionType.Complete,
            new DateOnly(2026, 8, 1), employeeId, assignedUser, Now);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await new GetEmployeeTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetEmployeeTasksRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(task.Id, item.Id);
        Assert.Equal("Full task", item.Title);
        Assert.Equal("Details here", item.Description);
        Assert.Equal("Open", item.Status);
        Assert.Equal("High", item.Priority);
        Assert.Equal("Onboarding", item.Source);
        Assert.Equal(new DateOnly(2026, 8, 1), item.DueDate);
        Assert.Equal(employeeId, item.AssignedEmployeeId);
        Assert.Equal(assignedUser, item.AssignedUserId);
        Assert.Equal(createdBy, item.CreatedBy);
        Assert.Null(item.CompletedBy);
        Assert.Null(item.CompletedAt);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
