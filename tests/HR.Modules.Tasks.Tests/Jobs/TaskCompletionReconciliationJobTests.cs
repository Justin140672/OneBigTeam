using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests.Jobs;

// Ticket 11 (P1): unit tests for TaskCompletionReconciliationJob, the recurring repair job that
// replays abandoned Pending TaskCompletionOperations and re-enqueues confirmation for abandoned
// DispatchApplied ones. Mirrors TaskCompletionEffectsJobTests's patterns (fresh InMemory database
// per test, so exact-count assertions are safe here — this project's fixtures are not shared across
// the test collection the way IdentityDatabaseFixture is).
public class TaskCompletionReconciliationJobTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(FixedNow);

    private static readonly DateTimeOffset Now = new(FixedNow);
    private static readonly DateTimeOffset StaleCreatedAt = Now - TimeSpan.FromMinutes(10);
    private static readonly DateTimeOffset FreshCreatedAt = Now - TimeSpan.FromMinutes(1);

    /// <summary>Stub ITaskCompletionAction used to simulate a succeeding/failing dispatch action for
    /// a given (Source, ActionType) pair. Mirrors CompleteTaskHandlerTests.StubCompletionAction.</summary>
    private sealed class StubCompletionAction(TaskSource source, TaskActionType actionType, Result result) : ITaskCompletionAction
    {
        public TaskSource Source => source;
        public TaskActionType ActionType => actionType;
        public Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }

    private static TaskCompletionReconciliationJob BuildJob(
        TasksDbContext context,
        TaskCompletionDispatcher? dispatcher = null,
        RecordingBackgroundJobClient? jobClient = null) =>
        new(context, dispatcher ?? new TaskCompletionDispatcher(Enumerable.Empty<ITaskCompletionAction>()),
            Clock, jobClient ?? new RecordingBackgroundJobClient(), NullLogger<TaskCompletionReconciliationJob>.Instance);

    private static TaskItem MakeOpenTask(Guid companyId, Guid assignedEmployee, Guid? sourceEntityId = null) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Review leave request", null, TaskPriority.Medium, TaskSource.Leave, TaskActionType.Approve,
            null, assignedEmployee, null, DateTimeOffset.UtcNow, sourceEntityId: sourceEntityId ?? Guid.NewGuid());

    // ---- Stale Pending: replay ----

    [Fact]
    public async Task ExecuteAsync_Replays_Stale_Pending_Operation_And_Completes_Task_On_Dispatch_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, StaleCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var succeedingDispatcher = new TaskCompletionDispatcher(
            [new StubCompletionAction(TaskSource.Leave, TaskActionType.Approve, Result.Success())]);
        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, succeedingDispatcher, jobClient).ExecuteAsync();

        var reloadedOperation = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, reloadedOperation.Status);

        var reloadedTask = await context.TaskItems.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Completed, reloadedTask.Status);
        Assert.Equal(completedBy, reloadedTask.CompletedBy);

        var enqueued = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(typeof(TaskCompletionEffectsJob), enqueued.Type);
        Assert.Equal(operation.Id, enqueued.Args[0]);
        Assert.Equal(companyId, enqueued.Args[1]);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Stale_Pending_Operation_Rejected_And_Leaves_Task_Uncompleted_On_Dispatch_Failure()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, StaleCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        const string failureReason = "The leave request is no longer pending.";
        var failingDispatcher = new TaskCompletionDispatcher(
            [new StubCompletionAction(TaskSource.Leave, TaskActionType.Approve,
                Result.Failure(Error.Conflict(failureReason)))]);
        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, failingDispatcher, jobClient).ExecuteAsync();

        var reloadedOperation = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusRejected, reloadedOperation.Status);
        Assert.Equal(failureReason, reloadedOperation.FailureReason);

        var reloadedTask = await context.TaskItems.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Open, reloadedTask.Status);
        Assert.Null(reloadedTask.CompletedBy);

        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Sweep_A_Fresh_Pending_Operation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, FreshCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        // A dispatcher that would fail the test if invoked — proves the fresh Pending operation was
        // never touched.
        var alwaysFailingDispatcher = new TaskCompletionDispatcher(
            [new StubCompletionAction(TaskSource.Leave, TaskActionType.Approve,
                Result.Failure(Error.Validation("must not be invoked")))]);

        await BuildJob(context, alwaysFailingDispatcher).ExecuteAsync();

        var reloadedOperation = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusPending, reloadedOperation.Status);

        var reloadedTask = await context.TaskItems.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Open, reloadedTask.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Stale_Pending_Operation_Rejected_When_TaskItem_No_Longer_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        // No TaskItem seeded — it no longer exists.
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, Guid.NewGuid(), completedBy, "Approve", null, StaleCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, jobClient: jobClient).ExecuteAsync();

        var reloadedOperation = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusRejected, reloadedOperation.Status);
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Stale_Pending_Operation_DispatchApplied_Without_ReDispatch_When_TaskItem_Already_Completed()
    {
        // The TaskItem was already completed by some other path — nothing to redo; the dispatcher
        // must not be invoked a second time.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        task.Complete(completedBy, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, StaleCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var alwaysFailingDispatcher = new TaskCompletionDispatcher(
            [new StubCompletionAction(TaskSource.Leave, TaskActionType.Approve,
                Result.Failure(Error.Validation("must not be invoked")))]);
        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, alwaysFailingDispatcher, jobClient).ExecuteAsync();

        var reloadedOperation = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, reloadedOperation.Status);

        // Converging the operation forward does not itself enqueue TaskCompletionEffectsJob — only
        // an actual replay dispatch does (see ReplayPendingAsync).
        Assert.Empty(jobClient.CreatedJobs);
    }

    // ---- Abandoned DispatchApplied: re-enqueue confirmation ----

    [Fact]
    public async Task ExecuteAsync_ReEnqueues_TaskCompletionEffectsJob_For_Abandoned_DispatchApplied_Operation_With_Null_LastAttemptAt()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        task.Complete(completedBy, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);

        // CreatePending sets LastAttemptAt to null; never call MarkDispatchApplied/RecordAttempt so
        // it stays null while Status is set to DispatchApplied directly via reflection-free means:
        // use MarkDispatchApplied but at a time far enough in the past that any freshness threshold
        // is irrelevant here — the important thing under test is the null-LastAttemptAt branch, so
        // seed without ever attempting, using CreatePending + MarkDispatchApplied at StaleCreatedAt.
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, StaleCreatedAt);
        operation.MarkDispatchApplied(StaleCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, jobClient: jobClient).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(typeof(TaskCompletionEffectsJob), enqueued.Type);
        Assert.Equal(operation.Id, enqueued.Args[0]);
        Assert.Equal(companyId, enqueued.Args[1]);
    }

    [Fact]
    public async Task ExecuteAsync_ReEnqueues_TaskCompletionEffectsJob_For_Abandoned_DispatchApplied_Operation_With_Old_LastAttemptAt()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        task.Complete(completedBy, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, StaleCreatedAt);
        operation.MarkDispatchApplied(StaleCreatedAt);
        // A subsequent attempt was recorded, but it too is stale.
        operation.RecordAttempt(StaleCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, jobClient: jobClient).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(operation.Id, enqueued.Args[0]);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_ReEnqueue_A_Fresh_DispatchApplied_Operation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeOpenTask(companyId, assignedEmployee);
        task.Complete(completedBy, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, "Approve", null, FreshCreatedAt);
        operation.MarkDispatchApplied(FreshCreatedAt);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(context, jobClient: jobClient).ExecuteAsync();

        Assert.Empty(jobClient.CreatedJobs);
    }
}
