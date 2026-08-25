using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class TaskReassignerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static TaskReassigner BuildReassigner(
        TasksDbContext dbContext, FakeNotificationWriter? notificationWriter = null) =>
        new(dbContext, notificationWriter ?? new FakeNotificationWriter(), new FakeClock(FixedUtcNow));

    private static TaskItem MakeTask(
        Guid companyId,
        Guid assignedEmployeeId,
        TaskItemStatus status = TaskItemStatus.Open,
        string title = "Task")
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, TaskSource.Offboarding, TaskActionType.Complete,
            dueDate: null, assignedEmployeeId, assignedEmployeeId, Now.AddDays(-5), null);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Reassigns_Open_Tasks_For_Assignee()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(1, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(toEmployeeId, saved.AssignedEmployeeId);
        Assert.Equal(toEmployeeId, saved.AssignedUserId);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Leaves_Completed_Tasks_Untouched()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Completed);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(fromEmployeeId, saved.AssignedEmployeeId);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Leaves_Cancelled_Tasks_Untouched()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Cancelled);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(fromEmployeeId, saved.AssignedEmployeeId);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Unassigns_When_ToEmployeeId_Is_Null()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, null, CancellationToken.None);

        Assert.Equal(1, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Null(saved.AssignedEmployeeId);
        Assert.Null(saved.AssignedUserId);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Does_Not_Touch_Task_From_Different_Company()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(otherCompanyId, fromEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(fromEmployeeId, saved.AssignedEmployeeId);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Does_Not_Touch_Task_Assigned_To_Different_Employee()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, otherEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(otherEmployeeId, saved.AssignedEmployeeId);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Returns_Correct_Count_For_Multiple_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task1 = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Open, title: "Task 1");
        var task2 = MakeTask(companyId, fromEmployeeId, TaskItemStatus.InProgress, title: "Task 2");
        var completedTask = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Completed, title: "Task 3");
        dbContext.TaskItems.AddRange(task1, task2, completedTask);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var count = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Is_Idempotent_On_Retry()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = BuildReassigner(dbContext);

        var firstCount = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);
        var secondCount = await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Writes_Notification_When_ToEmployeeId_Is_Set()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var toEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var reassigner = BuildReassigner(dbContext, notificationWriter);

        await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, toEmployeeId, CancellationToken.None);

        var written = Assert.Single(notificationWriter.Written);
        Assert.Equal(toEmployeeId, written.EmployeeId);
        Assert.Equal(NotificationType.TaskAssigned, written.Type);
    }

    [Fact]
    public async Task ReassignAllByAssigneeAsync_Does_Not_Write_Notification_When_ToEmployeeId_Is_Null()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var fromEmployeeId = Guid.NewGuid();
        var task = MakeTask(companyId, fromEmployeeId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var reassigner = BuildReassigner(dbContext, notificationWriter);

        await reassigner.ReassignAllByAssigneeAsync(companyId, fromEmployeeId, null, CancellationToken.None);

        Assert.Empty(notificationWriter.Written);
    }
}
