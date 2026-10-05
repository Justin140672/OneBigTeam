using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class TaskCompleterReconciliationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private sealed class CountingAction(TaskSource source, TaskActionType actionType) : ITaskCompletionAction
    {
        public TaskSource Source => source;
        public TaskActionType ActionType => actionType;
        public bool Fail { get; set; }
        public int Succeeded { get; private set; }

        public Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
        {
            if (Fail)
                throw new InvalidOperationException("Dispatch failed.");
            Succeeded++;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class Harness
    {
        public TasksDbContext Db { get; } =
            new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        public FakeNotificationWriter Notifications { get; } = new();
        public FakeAuditPublisher Audit { get; } = new();
        public CountingAction Action { get; } = new(TaskSource.Recruitment, TaskActionType.Complete);
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid InterviewId { get; } = Guid.NewGuid();

        public TaskCompleter Completer =>
            new(Db, Notifications, new FakeClock(FixedUtcNow), new TaskCompletionAuditDelivery(Audit, Audit), new TaskCompletionDispatcher([Action]));

        public TaskCanceller Canceller => new(Db, Notifications, new FakeClock(FixedUtcNow));

        public Task<bool> CompleteAsync(Guid? companyId = null, TaskActionType actionType = TaskActionType.Complete) =>
            Completer.CompleteConfirmedAsync(
                companyId ?? CompanyId, InterviewId, TaskSource.Recruitment, actionType, null, Guid.NewGuid(), CancellationToken.None);

        public async Task<TaskItem> AddTaskAsync(
            TaskActionType actionType = TaskActionType.Complete,
            TaskItemStatus status = TaskItemStatus.Open,
            Guid? companyId = null,
            bool withNotifications = true)
        {
            var task = TaskItem.Create(
                Guid.NewGuid(), companyId ?? CompanyId, Guid.NewGuid(), "Task", null, TaskPriority.Medium,
                TaskSource.Recruitment, actionType, null, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-5), InterviewId);
            if (status == TaskItemStatus.Completed) task.Complete(Guid.NewGuid(), Now);
            if (status == TaskItemStatus.Cancelled) task.Cancel(Now);
            Db.TaskItems.Add(task);
            await Db.SaveChangesAsync();

            if (withNotifications)
                foreach (var type in OpenTypes)
                    await Notifications.WriteAsync(
                        Guid.NewGuid(), task.CompanyId, task.AssignedEmployeeId!.Value, "n", null, task.Id, type,
                        NotificationPriority.Normal, Now);
            return task;
        }

        public int OpenNotificationCount(Guid taskId) =>
            Notifications.Written.Count(n => n.SourceEntityId == taskId && OpenTypes.Contains(n.Type));

        public int CompletionNotificationCount(Guid taskId) =>
            Notifications.Written.Count(n => n.SourceEntityId == taskId && n.Type == NotificationType.TaskCompleted);
    }

    private static readonly NotificationType[] OpenTypes =
        [NotificationType.TaskAssigned, NotificationType.TaskDueSoon, NotificationType.TaskOverdue];

    private static void AssertFinal(Harness h, TaskItem task)
    {
        Assert.Equal(TaskItemStatus.Completed, h.Db.TaskItems.Single(t => t.Id == task.Id).Status);
        Assert.Equal(0, h.OpenNotificationCount(task.Id));
        Assert.Equal(1, h.CompletionNotificationCount(task.Id));
        Assert.Single(h.Audit.Published);
        Assert.Equal(1, h.Action.Succeeded);
        Assert.NotNull(h.Db.ProgrammaticTaskCompletions.Single(c => c.TaskId == task.Id).ConfirmedAt);
    }

    [Theory]
    [InlineData(NotificationType.TaskAssigned)]
    [InlineData(NotificationType.TaskDueSoon)]
    [InlineData(NotificationType.TaskOverdue)]
    public async Task Failure_Removing_Each_Open_Notification_Type_Is_Repaired_On_Retry(NotificationType failing)
    {
        var h = new Harness();
        var task = await h.AddTaskAsync();
        h.Notifications.ThrowOnRemoveFor = failing;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());
        Assert.Equal(TaskItemStatus.Completed, (await h.Db.TaskItems.SingleAsync()).Status);

        h.Notifications.ThrowOnRemoveFor = null;
        Assert.True(await h.CompleteAsync());
        Assert.True(await h.CompleteAsync());

        AssertFinal(h, task);
    }

    [Fact]
    public async Task Failure_Writing_Completion_Notification_Is_Repaired_Without_Duplicates()
    {
        var h = new Harness();
        var task = await h.AddTaskAsync();
        h.Notifications.ThrowOnWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());
        Assert.Empty(h.Audit.Published);

        h.Notifications.ThrowOnWrite = false;
        await h.CompleteAsync();
        await h.CompleteAsync();

        AssertFinal(h, task);
    }

    [Fact]
    public async Task Failure_Publishing_Task_Audit_Is_Repaired_Without_Duplicate_Notification_Or_Audit()
    {
        var h = new Harness();
        var task = await h.AddTaskAsync();
        h.Audit.ThrowOnPublish = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());
        Assert.Equal(1, h.CompletionNotificationCount(task.Id));

        h.Audit.ThrowOnPublish = false;
        await h.CompleteAsync();
        await h.CompleteAsync();

        AssertFinal(h, task);
        Assert.Equal(task.Id, ((IAuditEvent)h.Audit.Published[0]).EventId);
    }

    [Fact]
    public async Task Failure_Dispatching_Completion_Action_Is_Repaired_And_Dispatch_Runs_Once()
    {
        var h = new Harness();
        var task = await h.AddTaskAsync();
        h.Action.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());
        Assert.Equal(0, h.Action.Succeeded);

        h.Action.Fail = false;
        await h.CompleteAsync();
        await h.CompleteAsync();
        await h.CompleteAsync();

        AssertFinal(h, task);
    }

    [Fact]
    public async Task Fully_Confirmed_Completed_Task_Is_A_NoOp()
    {
        var h = new Harness();
        var task = await h.AddTaskAsync();
        await h.CompleteAsync();
        var writes = h.Notifications.Written.Count;

        Assert.True(await h.CompleteAsync());

        Assert.Equal(writes, h.Notifications.Written.Count);
        AssertFinal(h, task);
    }

    [Fact]
    public async Task Completed_Task_Without_Programmatic_State_Reports_Unconfirmed_While_Its_Operation_Has_Pending_Effects()
    {
        var h = new Harness();
        var task = await h.AddTaskAsync(status: TaskItemStatus.Completed);
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), h.CompanyId, task.Id, Guid.NewGuid(), null, null, Now);
        operation.MarkDispatchApplied(Now);
        h.Db.TaskCompletionOperations.Add(operation);
        await h.Db.SaveChangesAsync();

        Assert.False(await h.CompleteAsync());
        Assert.Equal(0, h.OpenNotificationCount(task.Id));
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task Cancelled_Task_Is_Never_Completed_And_Missing_Task_Is_A_NoOp()
    {
        var h = new Harness();
        await h.AddTaskAsync(status: TaskItemStatus.Cancelled);

        Assert.True(await h.CompleteAsync());
        Assert.True(await h.Completer.CompleteConfirmedAsync(
            h.CompanyId, Guid.NewGuid(), TaskSource.Recruitment, TaskActionType.Complete, null, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(TaskItemStatus.Cancelled, (await h.Db.TaskItems.SingleAsync()).Status);
        Assert.Empty(h.Audit.Published);
        Assert.Empty(h.Db.ProgrammaticTaskCompletions);
    }

    [Fact]
    public async Task Completion_Is_Scoped_To_Company_Source_And_ActionType()
    {
        var h = new Harness();
        var otherCompany = await h.AddTaskAsync(companyId: Guid.NewGuid());
        var review = await h.AddTaskAsync(TaskActionType.Review);

        await h.CompleteAsync();

        Assert.Equal(TaskItemStatus.Open, (await h.Db.TaskItems.SingleAsync(t => t.Id == otherCompany.Id)).Status);
        Assert.Equal(TaskItemStatus.Open, (await h.Db.TaskItems.SingleAsync(t => t.Id == review.Id)).Status);
        Assert.Equal(3, h.OpenNotificationCount(review.Id));
        Assert.Equal(3, h.OpenNotificationCount(otherCompany.Id));
    }

    [Fact]
    public async Task Cancel_Removes_All_Open_Notification_Types_And_Retry_Recovers_A_Removal_Failure()
    {
        var h = new Harness();
        var task = await h.AddTaskAsync(TaskActionType.Review);
        h.Notifications.ThrowOnRemoveFor = NotificationType.TaskOverdue;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Canceller.CancelManyBySourceEntitiesAsync(
            h.CompanyId, [h.InterviewId], TaskSource.Recruitment, TaskActionType.Review, CancellationToken.None));
        Assert.Equal(TaskItemStatus.Cancelled, (await h.Db.TaskItems.SingleAsync()).Status);
        Assert.Equal(1, h.OpenNotificationCount(task.Id));

        h.Notifications.ThrowOnRemoveFor = null;
        var count = await h.Canceller.CancelManyBySourceEntitiesAsync(
            h.CompanyId, [h.InterviewId], TaskSource.Recruitment, TaskActionType.Review, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, h.OpenNotificationCount(task.Id));
    }

    [Fact]
    public async Task Cancel_Single_And_All_Variants_Remove_Assigned_Notifications_Of_Already_Cancelled_Tasks()
    {
        var h = new Harness();
        var single = await h.AddTaskAsync(TaskActionType.Review, TaskItemStatus.Cancelled);

        await h.Canceller.CancelBySourceEntityAsync(
            h.CompanyId, h.InterviewId, TaskSource.Recruitment, TaskActionType.Review, CancellationToken.None);
        Assert.Equal(0, h.OpenNotificationCount(single.Id));

        foreach (var type in OpenTypes)
            await h.Notifications.WriteAsync(
                Guid.NewGuid(), h.CompanyId, Guid.NewGuid(), "n", null, single.Id, type, NotificationPriority.Normal, Now);
        await h.Canceller.CancelAllBySourceEntityAsync(
            h.CompanyId, h.InterviewId, TaskSource.Recruitment, TaskActionType.Review, CancellationToken.None);

        Assert.Equal(0, h.OpenNotificationCount(single.Id));
    }

    [Fact]
    public async Task Cancel_Never_Touches_Completed_Tasks_Or_Their_Notifications_And_Is_Company_Scoped()
    {
        var h = new Harness();
        var completed = await h.AddTaskAsync(TaskActionType.Review, TaskItemStatus.Completed);
        var otherCompany = await h.AddTaskAsync(TaskActionType.Review, companyId: Guid.NewGuid());

        await h.Canceller.CancelManyBySourceEntitiesAsync(
            h.CompanyId, [h.InterviewId], TaskSource.Recruitment, TaskActionType.Review, CancellationToken.None);

        Assert.Equal(TaskItemStatus.Completed, (await h.Db.TaskItems.SingleAsync(t => t.Id == completed.Id)).Status);
        Assert.Equal(3, h.OpenNotificationCount(completed.Id));
        Assert.Equal(TaskItemStatus.Open, (await h.Db.TaskItems.SingleAsync(t => t.Id == otherCompany.Id)).Status);
        Assert.Equal(3, h.OpenNotificationCount(otherCompany.Id));
    }
}
