using System.Collections.Concurrent;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class ScriptedCompletionAction : ITaskCompletionAction
{
    private int _calls;

    public TaskSource Source => TaskSource.Recruitment;
    public TaskActionType ActionType => TaskActionType.Complete;

    public Func<TaskCompletionContext, Task<Result>> Behavior { get; set; } = _ => Task.FromResult(Result.Success());

    public ConcurrentQueue<TaskCompletionContext> Contexts { get; } = new();

    public int Calls => _calls;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        Contexts.Enqueue(context);
        return await Behavior(context);
    }
}

internal sealed class ProgrammaticCompletionHarness(Func<TasksDbContext> dbFactory)
{
    public static readonly DateTime FixedUtcNow = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    public FakeAuditPublisher Audit { get; } = new();
    public ThreadSafeNotificationWriter Notifications { get; } = new();
    public ScriptedCompletionAction Action { get; } = new();
    public ListLogger<TaskCompleter> CompleterLog { get; } = new();
    public Guid CompanyId { get; } = Guid.NewGuid();
    public Guid InterviewId { get; } = Guid.NewGuid();

    public TasksDbContext NewDb() => dbFactory();

    public TaskCompleter NewCompleter(TasksDbContext db) =>
        new(db, Notifications, new FakeClock(FixedUtcNow), new TaskCompletionAuditDelivery(Audit, Audit), new TaskCompletionDispatcher([Action]), CompleterLog);

    public async Task<bool> CompleteAsync(
        TasksDbContext? db = null, bool alreadyApplied = false, Guid? companyId = null, Guid? sourceEntityId = null)
    {
        var ctx = db ?? NewDb();
        try
        {
            return await NewCompleter(ctx).CompleteConfirmedAsync(
                companyId ?? CompanyId, sourceEntityId ?? InterviewId, TaskSource.Recruitment, TaskActionType.Complete,
                null, Guid.NewGuid(), CancellationToken.None, alreadyApplied);
        }
        finally
        {
            if (db is null)
                await ctx.DisposeAsync();
        }
    }

    public TaskCompletionRecovery NewRecovery(TasksDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), Audit,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskCompletionRecovery>.Instance);

    public async Task<TaskResolutionResult> ResolveAsync(
        TasksDbContext? db = null, bool alreadyApplied = false, Guid? sourceEntityId = null)
    {
        var ctx = db ?? NewDb();
        try
        {
            return await NewCompleter(ctx).ResolveAsync(
                CompanyId, sourceEntityId ?? InterviewId, TaskSource.Recruitment, TaskActionType.Complete,
                null, Guid.NewGuid(), CancellationToken.None, alreadyApplied);
        }
        finally
        {
            if (db is null)
                await ctx.DisposeAsync();
        }
    }

    public async Task<TaskCompletionResetResult> ResetAsync(
        Guid operationId, Guid? operatorUserId = null, string reason = "retry after fix", TasksDbContext? db = null)
    {
        var ctx = db ?? NewDb();
        try
        {
            return await NewRecovery(ctx).ResetTerminalCompletionAsync(
                CompanyId, operationId, operatorUserId ?? Guid.NewGuid(), reason, CancellationToken.None);
        }
        finally
        {
            if (db is null)
                await ctx.DisposeAsync();
        }
    }

    public async Task<TaskItem> AddTaskAsync(TaskItemStatus status = TaskItemStatus.Open)
    {
        await using var db = NewDb();
        var task = TaskItem.Create(
            Guid.NewGuid(), CompanyId, Guid.NewGuid(), "Record feedback", null, TaskPriority.Medium,
            TaskSource.Recruitment, TaskActionType.Complete, null, null, Guid.NewGuid(), Now.AddDays(-5), InterviewId);
        if (status == TaskItemStatus.Completed)
            task.Complete(Guid.NewGuid(), Now);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task AddOperationAsync(Guid taskId, string status)
    {
        await using var db = NewDb();
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), CompanyId, taskId, Guid.NewGuid(), null, null, Now);
        if (status == TaskCompletionOperation.StatusDispatchApplied)
            operation.MarkDispatchApplied(Now);
        else if (status == TaskCompletionOperation.StatusRejected)
            operation.MarkRejected("rejected", Now);
        else if (status == TaskCompletionOperation.StatusProcessed)
            operation.MarkProcessed(Now);
        db.TaskCompletionOperations.Add(operation);
        await db.SaveChangesAsync();
    }

    public async Task<ProgrammaticTaskCompletion?> StateAsync(Guid taskId)
    {
        await using var db = NewDb();
        return await db.ProgrammaticTaskCompletions.AsNoTracking().SingleOrDefaultAsync(c => c.TaskId == taskId);
    }

    public async Task<int> StateCountAsync()
    {
        await using var db = NewDb();
        return await db.ProgrammaticTaskCompletions.CountAsync(c => c.CompanyId == CompanyId);
    }

    public async Task<TaskItemStatus> TaskStatusAsync(Guid taskId)
    {
        await using var db = NewDb();
        return (await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status;
    }

    public ProgrammaticTaskCompletionReconciliationJob NewJob(TasksDbContext db) =>
        new(db, NewCompleter(db), new FakeClock(FixedUtcNow),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProgrammaticTaskCompletionReconciliationJob>.Instance);

    /// <summary>Persists an unconfirmed state, optionally claimed at <paramref name="claimedAt"/>.</summary>
    public async Task<ProgrammaticTaskCompletion> AddUnconfirmedStateAsync(TaskItem task, DateTimeOffset? claimedAt = null)
    {
        await using var db = NewDb();
        var tracked = await db.TaskItems.SingleAsync(t => t.Id == task.Id);
        var state = ProgrammaticTaskCompletion.Create(task.Id, CompanyId, Guid.NewGuid(), "Open", Now);
        if (claimedAt is { } at)
            state.Claim(Guid.NewGuid(), at);
        tracked.Complete(state.CompletedBy, Now);
        db.ProgrammaticTaskCompletions.Add(state);
        await db.SaveChangesAsync();
        return state;
    }
}
