using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class TaskCancellerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static TaskCanceller BuildCanceller(
        TasksDbContext dbContext, FakeNotificationWriter? notificationWriter = null) =>
        new(dbContext, notificationWriter ?? new FakeNotificationWriter(), new FakeClock(FixedUtcNow));

    private static TaskItem MakeTask(
        Guid companyId,
        Guid? sourceEntityId,
        TaskItemStatus status = TaskItemStatus.Open,
        TaskSource source = TaskSource.Offboarding,
        TaskActionType actionType = TaskActionType.Complete,
        Guid? assignedEmployeeId = null,
        Guid? assignedUserId = null,
        string title = "Task")
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, source, actionType,
            dueDate: null, assignedEmployeeId, assignedUserId, Now.AddDays(-5), sourceEntityId);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Cancels_Open_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(1, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Leaves_Completed_Tasks_Untouched()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Completed);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Completed, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Cancels_Overdue_Task_And_Removes_Its_Notifications()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        await notificationWriter.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Due soon", null, task.Id,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now.AddDays(-1));
        await notificationWriter.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Overdue", null, task.Id,
            NotificationType.TaskOverdue, NotificationPriority.High, Now);

        var canceller = BuildCanceller(dbContext, notificationWriter);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Cancels_Unassigned_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open, assignedEmployeeId: null, assignedUserId: null);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(1, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Is_Idempotent_On_Retry()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var firstCount = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);
        var secondCount = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Does_Not_Touch_Task_From_Different_Company()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(otherCompanyId, sourceEntityId, TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Open, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Does_Not_Touch_Task_With_Different_Source()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open, source: TaskSource.Workflow);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Open, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Does_Not_Touch_Task_With_Different_ActionType()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open, actionType: TaskActionType.Acknowledge);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Open, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Returns_Zero_For_Empty_Id_List_Without_Querying()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var task = MakeTask(companyId, Guid.NewGuid(), TaskItemStatus.Open);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Open, saved.Status);
    }

    [Fact]
    public async Task CancelManyBySourceEntitiesAsync_Cancels_Multiple_Tasks_Across_Multiple_SourceEntityIds()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId1 = Guid.NewGuid();
        var sourceEntityId2 = Guid.NewGuid();
        var task1 = MakeTask(companyId, sourceEntityId1, TaskItemStatus.Open, title: "Task 1");
        var task2 = MakeTask(companyId, sourceEntityId2, TaskItemStatus.InProgress, title: "Task 2");
        dbContext.TaskItems.AddRange(task1, task2);
        await dbContext.SaveChangesAsync();

        var canceller = BuildCanceller(dbContext);

        var count = await canceller.CancelManyBySourceEntitiesAsync(
            companyId, [sourceEntityId1, sourceEntityId2], TaskSource.Offboarding, TaskActionType.Complete, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(TaskItemStatus.Cancelled, (await dbContext.TaskItems.SingleAsync(t => t.Id == task1.Id)).Status);
        Assert.Equal(TaskItemStatus.Cancelled, (await dbContext.TaskItems.SingleAsync(t => t.Id == task2.Id)).Status);
    }
}
