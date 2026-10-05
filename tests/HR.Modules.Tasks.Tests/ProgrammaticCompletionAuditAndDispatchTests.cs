using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Tests;

public class ProgrammaticCompletionAuditAndDispatchTests
{
    private static ProgrammaticCompletionHarness NewHarness()
    {
        var dbName = Guid.NewGuid().ToString("N");
        return new ProgrammaticCompletionHarness(() =>
            new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(dbName).Options));
    }

    // Audit: a publisher that swallows a persistence failure (like DbAuditEventPublisher) must never
    // produce a confirmed checkpoint.

    [Fact]
    public async Task Swallowed_Audit_Persistence_Failure_Leaves_AuditPublishedAt_Unset_And_Completion_Unconfirmed()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Audit.SwallowPersistenceFailure = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());

        var state = await h.StateAsync(task.Id);
        Assert.Null(state!.AuditPublishedAt);
        Assert.Null(state.ConfirmedAt);
        Assert.Null(state.DispatchedAt);
        Assert.NotNull(state.FailureReason);
        Assert.False(state.IsTerminallyFailed);
        Assert.Equal(0, h.Action.Calls);
    }

    [Fact]
    public async Task Existence_Reader_Reporting_False_After_Publish_Leaves_Audit_Outstanding()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Audit.ExistsOverride = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());

        Assert.Equal(1, h.Audit.PublishCalls);
        Assert.Null((await h.StateAsync(task.Id))!.AuditPublishedAt);
    }

    [Fact]
    public async Task Existence_Reader_Reporting_True_After_Publish_Marks_Audit_Confirmed()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();

        Assert.True(await h.CompleteAsync());

        var state = await h.StateAsync(task.Id);
        Assert.NotNull(state!.AuditPublishedAt);
        Assert.NotNull(state.ConfirmedAt);
        Assert.Equal(1, h.Audit.PublishCalls);
    }

    [Fact]
    public async Task Already_Existing_Audit_Event_Is_Confirmed_Without_Republishing()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Audit.Seed(new TaskCompletedAuditEvent(h.CompanyId, task.Id, Guid.NewGuid(), "Open", null, ProgrammaticCompletionHarness.Now));

        Assert.True(await h.CompleteAsync());

        Assert.Equal(0, h.Audit.PublishCalls);
        Assert.NotNull((await h.StateAsync(task.Id))!.AuditPublishedAt);
    }

    [Fact]
    public async Task Audit_Failure_Then_Success_Recovers_And_Publishes_Exactly_One_Task_Completed_Event()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Audit.SwallowPersistenceFailure = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());

        h.Audit.SwallowPersistenceFailure = false;
        Assert.True(await h.CompleteAsync());
        Assert.True(await h.CompleteAsync());
        Assert.True(await h.CompleteAsync());

        Assert.Single(h.Audit.Published);
        Assert.Equal(task.Id, ((IAuditEvent)h.Audit.Published[0]).EventId);
        Assert.Null((await h.StateAsync(task.Id))!.FailureReason);
    }

    // Dispatch.

    [Fact]
    public async Task Failure_Result_From_Action_Leaves_DispatchedAt_And_ConfirmedAt_Unset()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Unexpected("downstream unavailable")));

        var ex = await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());

        Assert.False(ex.IsTerminal);
        var state = await h.StateAsync(task.Id);
        Assert.Null(state!.DispatchedAt);
        Assert.Null(state.ConfirmedAt);
        Assert.Contains("downstream unavailable", state.FailureReason);
        Assert.False(state.IsTerminallyFailed);
        Assert.Null(state.ClaimedBy);
    }

    [Fact]
    public async Task Throwing_Action_Leaves_DispatchedAt_And_ConfirmedAt_Unset_And_Records_Failure()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CompleteAsync());

        var state = await h.StateAsync(task.Id);
        Assert.Null(state!.DispatchedAt);
        Assert.Null(state.ConfirmedAt);
        Assert.Equal("boom", state.FailureReason);
        Assert.Equal(1, state.AttemptCount);
    }

    [Fact]
    public async Task Transient_Failure_Then_Success_Recovers_And_Clears_Failure_Info()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        var fail = true;
        h.Action.Behavior = _ => Task.FromResult(fail ? Result.Failure(Error.Unexpected("transient")) : Result.Success());

        await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());
        fail = false;
        Assert.True(await h.CompleteAsync());

        var state = await h.StateAsync(task.Id);
        Assert.NotNull(state!.DispatchedAt);
        Assert.NotNull(state.ConfirmedAt);
        Assert.Null(state.FailureReason);
        Assert.Equal(2, state.AttemptCount);
    }

    [Fact]
    public async Task Permanent_Validation_Failure_Is_Terminal_Logged_At_Error_And_Not_Retried()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("Feedback outcome is required to complete this task.")));

        var first = await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());
        var second = await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());

        Assert.True(first.IsTerminal);
        Assert.True(second.IsTerminal);
        Assert.Equal(1, h.Action.Calls);
        var state = await h.StateAsync(task.Id);
        Assert.True(state!.IsTerminallyFailed);
        Assert.Null(state.DispatchedAt);
        Assert.Contains(h.CompleterLog.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains(task.Id.ToString()) && e.Message.Contains("failed permanently"));
    }

    [Fact]
    public async Task Retryable_Failures_Become_Terminal_After_The_Attempt_Bound()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Unexpected("still down")));

        for (var i = 0; i < TaskCompleter.MaxRetryableAttempts; i++)
            await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());

        Assert.True((await h.StateAsync(task.Id))!.IsTerminallyFailed);
        Assert.Equal(TaskCompleter.MaxRetryableAttempts, h.Action.Calls);

        await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());
        Assert.Equal(TaskCompleter.MaxRetryableAttempts, h.Action.Calls);
    }

    [Fact]
    public async Task Business_Effect_Already_Applied_Skips_Dispatch_But_Runs_Every_Other_Effect()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("Feedback outcome is required to complete this task.")));

        Assert.True(await h.CompleteAsync(alreadyApplied: true));

        Assert.Equal(0, h.Action.Calls);
        var state = await h.StateAsync(task.Id);
        Assert.Equal(ProgrammaticTaskCompletion.DispatchModeAlreadyApplied, state!.DispatchMode);
        Assert.NotNull(state.ConfirmedAt);
        Assert.Single(h.Audit.Published);
        Assert.Equal(TaskItemStatus.Completed, await h.TaskStatusAsync(task.Id));
    }

    [Fact]
    public async Task Dispatch_Receives_The_Same_Stable_NonEmpty_OperationId_Across_Retries()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        var fail = true;
        h.Action.Behavior = _ => Task.FromResult(fail ? Result.Failure(Error.Unexpected("transient")) : Result.Success());

        await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());
        fail = false;
        await h.CompleteAsync();

        var state = await h.StateAsync(task.Id);
        Assert.NotEqual(Guid.Empty, state!.OperationId);
        Assert.Equal(2, h.Action.Contexts.Count);
        Assert.All(h.Action.Contexts, c => Assert.Equal(state.OperationId, c.DispatchOperationId));
    }

    [Fact]
    public async Task Replay_Of_A_Confirmed_Completion_Does_Not_Duplicate_Downstream_Effects()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();

        for (var i = 0; i < 4; i++)
            Assert.True(await h.CompleteAsync());

        Assert.Equal(1, h.Action.Calls);
        Assert.Single(h.Audit.Published);
        Assert.Equal(1, await h.StateCountAsync());
        Assert.Equal(TaskItemStatus.Completed, await h.TaskStatusAsync(task.Id));
    }

    [Fact]
    public async Task Void_Completion_Path_Logs_A_Dispatch_Failure_At_Error_Instead_Of_Ignoring_It()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("bad")));

        await using var db = h.NewDb();
        await h.NewCompleter(db).CompleteBySourceEntityAsync(
            h.CompanyId, h.InterviewId, TaskSource.Recruitment, TaskActionType.Complete, Guid.NewGuid(), CancellationToken.None);

        Assert.Contains(h.CompleterLog.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("is outstanding"));
        Assert.Null((await h.StateAsync(task.Id))!.DispatchedAt);
    }

    [Fact]
    public async Task Reconciliation_Job_Retries_A_Retryable_Failure_And_Skips_Terminal_Ones()
    {
        var h = NewHarness();
        var retryable = await h.AddTaskAsync();
        var fail = true;
        h.Action.Behavior = _ => Task.FromResult(fail ? Result.Failure(Error.Unexpected("transient")) : Result.Success());
        await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());

        fail = false;
        await using (var db = h.NewDb())
            await h.NewJob(db).ExecuteAsync();

        Assert.NotNull((await h.StateAsync(retryable.Id))!.ConfirmedAt);

        var h2 = NewHarness();
        var terminal = await h2.AddTaskAsync();
        h2.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("never valid")));
        await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h2.CompleteAsync());

        await using (var db = h2.NewDb())
            await h2.NewJob(db).ExecuteAsync();

        Assert.Equal(1, h2.Action.Calls);
        Assert.Null((await h2.StateAsync(terminal.Id))!.ConfirmedAt);
    }

    // Coordination with CompleteTaskHandler's TaskCompletionOperation.

    [Fact]
    public async Task Pending_Operation_Reports_Unconfirmed_And_Creates_No_Competing_State()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        await h.AddOperationAsync(task.Id, TaskCompletionOperation.StatusPending);

        Assert.False(await h.CompleteAsync());

        Assert.Null(await h.StateAsync(task.Id));
        Assert.Equal(TaskItemStatus.Open, await h.TaskStatusAsync(task.Id));
        Assert.Equal(0, h.Action.Calls);
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task DispatchApplied_Operation_Stays_Unconfirmed_Without_Programmatic_State()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync(TaskItemStatus.Completed);
        await h.AddOperationAsync(task.Id, TaskCompletionOperation.StatusDispatchApplied);

        Assert.False(await h.CompleteAsync());

        Assert.Null(await h.StateAsync(task.Id));
        Assert.Equal(0, h.Action.Calls);
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task Processed_Operation_Is_Recognised_As_Confirmed_With_No_Programmatic_State()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync(TaskItemStatus.Completed);
        await h.AddOperationAsync(task.Id, TaskCompletionOperation.StatusProcessed);

        Assert.True(await h.CompleteAsync());

        Assert.Null(await h.StateAsync(task.Id));
        Assert.Equal(0, h.Action.Calls);
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task Rejected_Operation_Does_Not_Report_Completion_And_Allows_A_Later_Programmatic_Completion()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        await h.AddOperationAsync(task.Id, TaskCompletionOperation.StatusRejected);
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Unexpected("still failing")));

        await Assert.ThrowsAsync<TaskCompletionDispatchException>(() => h.CompleteAsync());
        Assert.NotNull(await h.StateAsync(task.Id));
        Assert.Null((await h.StateAsync(task.Id))!.ConfirmedAt);

        h.Action.Behavior = _ => Task.FromResult(Result.Success());
        Assert.True(await h.CompleteAsync());
        Assert.NotNull((await h.StateAsync(task.Id))!.ConfirmedAt);
    }
}
