using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class TaskReschedulerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static TaskRescheduler BuildRescheduler(
        TasksDbContext dbContext, FakeNotificationWriter? notificationWriter = null) =>
        new(dbContext, notificationWriter ?? new FakeNotificationWriter(), new FakeClock(FixedUtcNow));

    private static TaskItem MakeTask(
        Guid companyId,
        Guid? sourceEntityId,
        TaskItemStatus status = TaskItemStatus.Open,
        TaskSource source = TaskSource.Offboarding,
        TaskActionType actionType = TaskActionType.Complete,
        DateOnly? dueDate = null,
        Guid? assignedEmployeeId = null,
        Guid? assignedUserId = null,
        string title = "Task")
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, source, actionType,
            dueDate, assignedEmployeeId, assignedUserId, Now.AddDays(-5), sourceEntityId);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Reschedules_Open_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);
        var newDueDate = new DateOnly(2026, 8, 15);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, newDueDate, CancellationToken.None);

        Assert.Equal(1, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(newDueDate, saved.DueDate);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Leaves_Completed_Task_Untouched()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Completed, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.DueDate);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Leaves_Cancelled_Task_Untouched()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Cancelled, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.DueDate);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Does_Not_Touch_Task_With_Different_Source()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, source: TaskSource.Workflow, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.DueDate);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Does_Not_Touch_Task_With_Different_ActionType()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, actionType: TaskActionType.Acknowledge, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.DueDate);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Does_Not_Touch_Task_From_Different_Company()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(otherCompanyId, sourceEntityId, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.DueDate);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Returns_Zero_For_Empty_Id_List_Without_Querying()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var task = MakeTask(companyId, Guid.NewGuid(), dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(0, count);
        var saved = await dbContext.TaskItems.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.DueDate);
    }

    // Idempotency: calling again with the same date is a genuine no-op — returns 0 and does not
    // write a second TaskDateChanged notification.
    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Called_Twice_With_Same_Date_Is_Idempotent_And_Does_Not_Duplicate_Notification()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, dueDate: new DateOnly(2026, 8, 1), assignedEmployeeId: assignee);
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var rescheduler = BuildRescheduler(dbContext, notificationWriter);
        var newDueDate = new DateOnly(2026, 8, 15);

        var firstCount = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, newDueDate, CancellationToken.None);
        var secondCount = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, newDueDate, CancellationToken.None);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
        Assert.Single(notificationWriter.Written.Where(n => n.Type == NotificationType.TaskDateChanged));
    }

    // Dedup: two changed tasks assigned to the same employee should only trigger one TaskDateChanged
    // notification for that employee, not one per task.
    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Writes_One_TaskDateChanged_Notification_Per_Distinct_Assignee()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId1 = Guid.NewGuid();
        var sourceEntityId2 = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var task1 = MakeTask(companyId, sourceEntityId1, dueDate: new DateOnly(2026, 8, 1), assignedEmployeeId: assignee, title: "Task 1");
        var task2 = MakeTask(companyId, sourceEntityId2, dueDate: new DateOnly(2026, 8, 1), assignedEmployeeId: assignee, title: "Task 2");
        dbContext.TaskItems.AddRange(task1, task2);
        await dbContext.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        var rescheduler = BuildRescheduler(dbContext, notificationWriter);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId1, sourceEntityId2], TaskSource.Offboarding, TaskActionType.Complete,
            new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(2, count);
        var dateChangedNotifications = notificationWriter.Written.Where(n => n.Type == NotificationType.TaskDateChanged).ToList();
        var notification = Assert.Single(dateChangedNotifications);
        Assert.Equal(assignee, notification.EmployeeId);
    }

    // Stale TaskDueSoon/TaskOverdue notifications must be removed once a task's due date moves,
    // regardless of direction — they no longer reflect the new date.
    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Removes_Stale_DueSoon_And_Overdue_Notifications()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, dueDate: new DateOnly(2026, 8, 1));
        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var notificationWriter = new FakeNotificationWriter();
        await notificationWriter.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Due soon", null, task.Id,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now.AddDays(-1));
        await notificationWriter.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Overdue", null, task.Id,
            NotificationType.TaskOverdue, NotificationPriority.High, Now);

        var rescheduler = BuildRescheduler(dbContext, notificationWriter);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId], TaskSource.Offboarding, TaskActionType.Complete, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(1, count);
        Assert.DoesNotContain(notificationWriter.Written, n => n.Type is NotificationType.TaskDueSoon or NotificationType.TaskOverdue);
    }

    [Fact]
    public async Task RescheduleManyBySourceEntitiesAsync_Reschedules_Multiple_Tasks_Across_Multiple_SourceEntityIds()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId1 = Guid.NewGuid();
        var sourceEntityId2 = Guid.NewGuid();
        var task1 = MakeTask(companyId, sourceEntityId1, dueDate: new DateOnly(2026, 8, 1), title: "Task 1");
        var task2 = MakeTask(companyId, sourceEntityId2, TaskItemStatus.InProgress, dueDate: new DateOnly(2026, 8, 1), title: "Task 2");
        dbContext.TaskItems.AddRange(task1, task2);
        await dbContext.SaveChangesAsync();

        var rescheduler = BuildRescheduler(dbContext);
        var newDueDate = new DateOnly(2026, 8, 20);

        var count = await rescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, [sourceEntityId1, sourceEntityId2], TaskSource.Offboarding, TaskActionType.Complete, newDueDate, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(newDueDate, (await dbContext.TaskItems.SingleAsync(t => t.Id == task1.Id)).DueDate);
        Assert.Equal(newDueDate, (await dbContext.TaskItems.SingleAsync(t => t.Id == task2.Id)).DueDate);
    }
}
