using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class GetMyTasksHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static TaskItem MakeTask(
        Guid companyId,
        Guid? assignedUserId,
        string title = "Task",
        TaskItemStatus status = TaskItemStatus.Open,
        DateOnly? dueDate = null)
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, TaskSource.Manual, TaskActionType.Complete,
            dueDate, null, assignedUserId, Now);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task HandleAsync_Returns_Tasks_Assigned_To_User()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, userId,  "My task A"),
            MakeTask(companyId, userId,  "My task B"),
            MakeTask(companyId, otherId, "Someone else's task"),
            MakeTask(companyId, null,    "Unassigned task"));

        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(userId, item.AssignedUserId));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Tasks_From_Other_Companies()
    {
        await using var context = BuildContext();
        var userId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyA, userId, "Company A task"),
            MakeTask(companyB, userId, "Company B task"));

        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyA, UserId = userId },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Company A task", result.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Tasks_Assigned()
    {
        await using var context = BuildContext();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_When_Provided()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, userId, "Open task",       TaskItemStatus.Open),
            MakeTask(companyId, userId, "In progress",     TaskItemStatus.InProgress),
            MakeTask(companyId, userId, "Completed task",  TaskItemStatus.Completed));

        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId, Status = "Open" },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Open", result.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Status_Filter_Is_Case_Insensitive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, userId, "Open task", TaskItemStatus.Open),
            MakeTask(companyId, userId, "Completed",  TaskItemStatus.Completed));

        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId, Status = "open" },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Open", result.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Unknown_Status_And_Returns_All()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, userId, "Task A"),
            MakeTask(companyId, userId, "Task B"));

        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId, Status = "bogus" },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_DueDate_Then_CreatedAt_Nulls_Last()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, userId, "No due date",    dueDate: null),
            MakeTask(companyId, userId, "Due later",      dueDate: new DateOnly(2026, 9, 1)),
            MakeTask(companyId, userId, "Due soon",       dueDate: new DateOnly(2026, 7, 1)));

        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal("Due soon",    result.Items[0].Title);
        Assert.Equal("Due later",   result.Items[1].Title);
        Assert.Equal("No due date", result.Items[2].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Fields_Correctly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            "Full task", "With all fields",
            TaskPriority.Critical, TaskSource.Compliance, TaskActionType.Complete,
            new DateOnly(2026, 12, 1), assignedEmployee, userId, Now);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await new GetMyTasksHandler(context, new FakeEmployeeNameReader()).HandleAsync(
            new GetMyTasksRequest { CompanyId = companyId, UserId = userId },
            CancellationToken.None);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(task.Id, item.Id);
        Assert.Equal("Full task", item.Title);
        Assert.Equal("With all fields", item.Description);
        Assert.Equal("Open", item.Status);
        Assert.Equal("Critical", item.Priority);
        Assert.Equal("Compliance", item.Source);
        Assert.Equal(new DateOnly(2026, 12, 1), item.DueDate);
        Assert.Equal(assignedEmployee, item.AssignedEmployeeId);
        Assert.Equal(userId, item.AssignedUserId);
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
