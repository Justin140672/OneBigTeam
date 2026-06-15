using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetTeamTasks;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class GetTeamTasksHandlerTests
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
            title, null, TaskPriority.Medium, TaskSource.Manual,
            dueDate, assignedEmployeeId, null, Now);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task HandleAsync_Returns_Tasks_Assigned_To_Direct_Reports()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var report1 = Guid.NewGuid();
        var report2 = Guid.NewGuid();
        var other   = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, report1, "Report 1 task A"),
            MakeTask(companyId, report1, "Report 1 task B"),
            MakeTask(companyId, report2, "Report 2 task"),
            MakeTask(companyId, other,   "Outside team task"),
            MakeTask(companyId, null,    "Unassigned task"));

        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(report1, report2));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = managerId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item =>
            Assert.True(item.AssignedEmployeeId == report1 || item.AssignedEmployeeId == report2));
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Manager_Has_No_Direct_Reports()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.TaskItems.Add(MakeTask(companyId, Guid.NewGuid(), "Some task"));
        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Tasks_From_Other_Companies()
    {
        await using var context = BuildContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var reportId  = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyA, reportId, "Company A task"),
            MakeTask(companyB, reportId, "Company B task"));

        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(reportId));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyA, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Company A task", result.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_When_Provided()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var reportId  = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, reportId, "Open task",      TaskItemStatus.Open),
            MakeTask(companyId, reportId, "In progress",    TaskItemStatus.InProgress),
            MakeTask(companyId, reportId, "Completed task", TaskItemStatus.Completed));

        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(reportId));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = Guid.NewGuid(), Status = "InProgress" },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("InProgress", result.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Status_Filter_Is_Case_Insensitive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var reportId  = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, reportId, "Open task",  TaskItemStatus.Open),
            MakeTask(companyId, reportId, "Completed",  TaskItemStatus.Completed));

        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(reportId));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = Guid.NewGuid(), Status = "completed" },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Completed", result.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Unknown_Status_And_Returns_All()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var reportId  = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, reportId, "Task A"),
            MakeTask(companyId, reportId, "Task B"));

        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(reportId));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = Guid.NewGuid(), Status = "bogus" },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_DueDate_Then_CreatedAt_Nulls_Last()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var reportId  = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, reportId, "No due date", dueDate: null),
            MakeTask(companyId, reportId, "Due later",   dueDate: new DateOnly(2026, 9, 1)),
            MakeTask(companyId, reportId, "Due soon",    dueDate: new DateOnly(2026, 7, 1)));

        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(reportId));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal("Due soon",    result.Items[0].Title);
        Assert.Equal("Due later",   result.Items[1].Title);
        Assert.Equal("No due date", result.Items[2].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Fields_Correctly()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var reportId   = Guid.NewGuid();
        var assignedUser = Guid.NewGuid();
        var createdBy  = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            "Full team task", "Details here",
            TaskPriority.High, TaskSource.Workflow,
            new DateOnly(2026, 8, 15), reportId, assignedUser, Now);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, new FakeDirectReportsReader(reportId));

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(task.Id, item.Id);
        Assert.Equal("Full team task", item.Title);
        Assert.Equal("Details here", item.Description);
        Assert.Equal("Open", item.Status);
        Assert.Equal("High", item.Priority);
        Assert.Equal("Workflow", item.Source);
        Assert.Equal(new DateOnly(2026, 8, 15), item.DueDate);
        Assert.Equal(reportId, item.AssignedEmployeeId);
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
