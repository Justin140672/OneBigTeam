using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests.Jobs;

public class TaskCompletionEffectsTerminalTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedNow);
    private static readonly FakeClock Clock = new(FixedNow);

    private sealed class Rig
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

        public FakeAuditPublisher Audit { get; } = new();
        public FakeNotificationWriter Notifications { get; } = new();
        public ScriptedCompletionAction Action { get; } = new();
        public RecordingBackgroundJobClient JobClient { get; } = new();
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();

        public TasksDbContext NewDb() =>
            new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(_dbName).Options);

        public TaskCompletionEffectsJob EffectsJob(TasksDbContext db) =>
            new(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                NullLogger<TaskCompletionEffectsJob>.Instance);

        public TaskCompletionReconciliationJob ReconciliationJob(TasksDbContext db) =>
            new(db, new TaskCompletionDispatcher([Action]), Clock, JobClient,
                NullLogger<TaskCompletionReconciliationJob>.Instance);

        public TaskCompletionRecovery Recovery(TasksDbContext db) =>
            new(db, Clock,
                new TaskRecoveryAuditDelivery(db, Audit, Audit, Clock, NullLogger<TaskRecoveryAuditDelivery>.Instance),
                NullLogger<TaskCompletionRecovery>.Instance, null, JobClient);

        public async Task<(Guid OperationId, Guid TaskId)> SeedAsync(
            bool assigned = true, bool withSnapshot = true, bool keepTask = true)
        {
            await using var db = NewDb();
            var task = TaskItem.Create(
                Guid.NewGuid(), CompanyId, Guid.NewGuid(), "Review leave", null, TaskPriority.Medium,
                TaskSource.Leave, TaskActionType.Approve, null, assigned ? EmployeeId : null, null, Now);
            task.Complete(Guid.NewGuid(), Now);

            var operation = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), CompanyId, task.Id, task.CompletedBy!.Value, null, null, Now);
            operation.MarkDispatchApplied(Now);
            if (withSnapshot)
                operation.CaptureCompletionSnapshot(task.AssignedEmployeeId, task.Title, task.Description, "Open", Now, Now);

            if (keepTask)
                db.TaskItems.Add(task);
            db.TaskCompletionOperations.Add(operation);
            await db.SaveChangesAsync();
            return (operation.Id, task.Id);
        }

        public async Task<TaskCompletionOperation> LoadAsync(Guid operationId)
        {
            await using var db = NewDb();
            return await db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operationId);
        }

        public async Task RunEffectsAsync(Guid operationId, bool swallowFailure = false)
        {
            await using var db = NewDb();
            try
            {
                await EffectsJob(db).ProcessAsync(operationId, CompanyId);
            }
            catch (Exception) when (swallowFailure)
            {
            }
        }

        public async Task<TaskCompletionOperation> ExhaustAsync(Guid operationId)
        {
            for (var i = 0; i < TaskCompletionEffectsJob.MaxAttempts; i++)
                await RunEffectsAsync(operationId, swallowFailure: true);
            return await LoadAsync(operationId);
        }
    }

    [Fact]
    public async Task Retry_Limit_Persists_A_Terminal_State_Releases_The_Claim_And_Keeps_The_Task_Completed()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;

        var operation = await rig.ExhaustAsync(operationId);

        Assert.Equal(TaskCompletionOperation.StatusEffectsTerminalFailure, operation.Status);
        Assert.NotNull(operation.TerminalFailureAt);
        Assert.Equal(TaskCompletionOperation.CategoryEffectsRetryLimit, operation.FailureCategory);
        Assert.False(string.IsNullOrEmpty(operation.FailureReason));
        Assert.Null(operation.ClaimedBy);
        Assert.Null(operation.LeaseExpiresAt);
        await using var db = rig.NewDb();
        Assert.Equal(TaskItemStatus.Completed, (await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status);
    }

    [Fact]
    public async Task Final_Attempt_Does_Not_Rethrow_And_Later_Runs_Are_Skipped_Without_Side_Effects()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;
        await rig.ExhaustAsync(operationId);
        var publishCalls = rig.Audit.PublishCalls;

        await rig.RunEffectsAsync(operationId);

        Assert.Equal(publishCalls, rig.Audit.PublishCalls);
        Assert.Equal(TaskCompletionOperation.StatusEffectsTerminalFailure, (await rig.LoadAsync(operationId)).Status);
    }

    [Fact]
    public async Task Recurring_Reconciliation_Excludes_Terminal_Operations()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;
        await rig.ExhaustAsync(operationId);

        await using var db = rig.NewDb();
        await rig.ReconciliationJob(db).ExecuteAsync();

        Assert.Empty(rig.JobClient.CreatedJobs);
    }

    [Fact]
    public async Task Operator_Retry_Resumes_Only_Effects_Never_The_Business_Dispatch()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;
        await rig.ExhaustAsync(operationId);
        rig.Audit.ThrowOnPublish = false;

        TaskCompletionResetResult reset;
        await using (var db = rig.NewDb())
        {
            reset = await rig.Recovery(db).ResetTerminalCompletionAsync(
                rig.CompanyId, operationId, Guid.NewGuid(), "audit store restored", CancellationToken.None);
        }

        Assert.Equal(TaskCompletionResetOutcome.Reset, reset.Outcome);
        Assert.Equal(TaskRecoveryAction.KindInteractive, reset.OperationKind);
        var afterReset = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, afterReset.Status);
        Assert.Null(afterReset.TerminalFailureAt);
        Assert.Equal(0, afterReset.AttemptCount);
        Assert.Equal(1, afterReset.ResetCount);
        Assert.NotNull(afterReset.LastResetBy);
        Assert.Single(rig.JobClient.CreatedJobs);

        await rig.RunEffectsAsync(operationId);

        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.LoadAsync(operationId)).Status);
        Assert.Equal(0, rig.Action.Calls);
        Assert.Single(rig.Audit.Published.OfType<TaskCompletedAuditEvent>());
        Assert.Equal(taskId, rig.Audit.Published.OfType<TaskCompletedAuditEvent>().Single().TaskId);
    }

    [Fact]
    public async Task Reconciliation_After_A_Reset_Re_Enqueues_Effects_But_Does_Not_Dispatch()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;
        await rig.ExhaustAsync(operationId);

        await using (var db = rig.NewDb())
            await rig.Recovery(db).ResetTerminalCompletionAsync(
                rig.CompanyId, operationId, Guid.NewGuid(), "retry", CancellationToken.None);
        rig.JobClient.CreatedJobs.Clear();

        await using (var db = rig.NewDb())
            await rig.ReconciliationJob(db).ExecuteAsync();

        Assert.Single(rig.JobClient.CreatedJobs);
        Assert.Equal(0, rig.Action.Calls);
    }

    [Fact]
    public async Task Confirmed_Effects_Are_Not_Duplicated_After_A_Reset()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;
        await rig.ExhaustAsync(operationId);
        Assert.Single(rig.Notifications.Written);

        rig.Audit.ThrowOnPublish = false;
        await using (var db = rig.NewDb())
            await rig.Recovery(db).ResetTerminalCompletionAsync(
                rig.CompanyId, operationId, Guid.NewGuid(), "retry", CancellationToken.None);
        await rig.RunEffectsAsync(operationId);
        await rig.RunEffectsAsync(operationId);

        Assert.Single(rig.Notifications.Written.Where(n => n.SourceEntityId == taskId && n.Type == NotificationType.TaskCompleted));
        Assert.Single(rig.Audit.Published.OfType<TaskCompletedAuditEvent>());
    }

    [Fact]
    public async Task Recovery_Of_An_Interactive_Operation_Is_Audited_Durably()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync();
        rig.Audit.ThrowOnPublish = true;
        await rig.ExhaustAsync(operationId);
        rig.Audit.ThrowOnPublish = false;

        await using (var db = rig.NewDb())
            await rig.Recovery(db).ResetTerminalCompletionAsync(
                rig.CompanyId, operationId, Guid.NewGuid(), "retry", CancellationToken.None);

        await using var verify = rig.NewDb();
        var action = await verify.TaskRecoveryActions.AsNoTracking().SingleAsync();
        Assert.Equal(TaskRecoveryAction.KindInteractive, action.OperationKind);
        Assert.Equal(operationId, action.OperationId);
        Assert.Equal(taskId, action.TaskId);
        Assert.NotNull(action.AuditDeliveredAt);
        Assert.Single(rig.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
    }

    [Fact]
    public async Task Reset_Of_A_Data_Integrity_Failure_Is_Rejected_And_Leaves_It_Untouched()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(withSnapshot: false, keepTask: false);
        await rig.RunEffectsAsync(operationId);

        TaskCompletionResetResult reset;
        await using (var db = rig.NewDb())
            reset = await rig.Recovery(db).ResetTerminalCompletionAsync(
                rig.CompanyId, operationId, Guid.NewGuid(), "retry", CancellationToken.None);

        Assert.Equal(TaskCompletionResetOutcome.DataIntegrityFailure, reset.Outcome);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, (await rig.LoadAsync(operationId)).Status);
        await using var verify = rig.NewDb();
        Assert.Empty(await verify.TaskRecoveryActions.ToListAsync());
    }

    [Fact]
    public async Task Missing_Task_With_Audit_Present_But_Required_Notification_Absent_Is_Not_Processed()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(keepTask: false);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", rig.EmployeeId, Now));

        await rig.RunEffectsAsync(operationId);

        var operation = await rig.LoadAsync(operationId);
        Assert.NotEqual(TaskCompletionOperation.StatusProcessed, operation.Status);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, operation.Status);
        Assert.Equal(TaskCompletionOperation.CategoryNotificationUnconfirmed, operation.FailureCategory);
        Assert.Empty(rig.Notifications.Written);
    }

    [Fact]
    public async Task Missing_Task_With_Audit_And_Required_Notification_Both_Confirmed_Is_Processed()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(keepTask: false);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", rig.EmployeeId, Now));
        await rig.Notifications.WriteAsync(
            Guid.NewGuid(), rig.CompanyId, rig.EmployeeId, "Task completed: Review leave", null, taskId,
            NotificationType.TaskCompleted, NotificationPriority.Normal, Now);

        await rig.RunEffectsAsync(operationId);

        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.LoadAsync(operationId)).Status);
    }

    [Fact]
    public async Task Missing_Unassigned_Task_With_Audit_Present_Is_Processed()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(assigned: false, keepTask: false);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", null, Now));

        await rig.RunEffectsAsync(operationId);

        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.LoadAsync(operationId)).Status);
    }

    [Fact]
    public async Task Missing_Task_With_Audit_Absent_Is_A_Data_Integrity_Failure()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(assigned: false, keepTask: false);

        await rig.RunEffectsAsync(operationId);

        var operation = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, operation.Status);
        Assert.Equal(TaskCompletionOperation.CategoryAuditUnconfirmed, operation.FailureCategory);
    }

    [Fact]
    public async Task Legacy_Operation_Without_A_Snapshot_Is_A_Data_Integrity_Failure_Even_When_The_Audit_Exists()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(assigned: false, withSnapshot: false, keepTask: false);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", null, Now));

        await rig.RunEffectsAsync(operationId);
        await rig.RunEffectsAsync(operationId);

        var operation = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, operation.Status);
        Assert.Equal(TaskCompletionOperation.CategoryEvidenceMissing, operation.FailureCategory);
        Assert.NotNull(operation.TerminalFailureAt);
    }

    [Fact]
    public async Task Existing_Task_Backfills_The_Completion_Snapshot_For_A_Legacy_Operation()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(withSnapshot: false);

        await rig.RunEffectsAsync(operationId);

        var operation = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, operation.Status);
        Assert.True(operation.HasCompletionSnapshot);
        Assert.True(operation.NotificationRequired);
        Assert.Equal(rig.EmployeeId, operation.SnapshotAssignedEmployeeId);
        Assert.Equal("Review leave", operation.SnapshotTaskTitle);
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusEffectsTerminalFailure)]
    [InlineData(TaskCompletionOperation.StatusDataIntegrityFailure)]
    public async Task Typed_Resolution_Reports_Terminal_And_Data_Integrity_Operations_Instead_Of_Confirmed(string status)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var h = new ProgrammaticCompletionHarness(() =>
            new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(dbName).Options));
        var task = await h.AddTaskAsync(TaskItemStatus.Completed);
        var operationId = await h.AddOperationAsync(task.Id, status);

        var result = await h.ResolveAsync();

        Assert.Equal(TaskResolutionStatus.TerminalFailure, result.Status);
        Assert.False(result.IsConfirmed);
        Assert.Equal(operationId, result.OperationId);
        Assert.Equal(task.Id, result.TaskId);
        Assert.False(string.IsNullOrEmpty(result.FailureReason));
        Assert.NotNull(result.TerminalFailureAt);
        Assert.NotNull(result.FailureCategory);
        Assert.Equal(0, await h.StateCountAsync());
    }
}
