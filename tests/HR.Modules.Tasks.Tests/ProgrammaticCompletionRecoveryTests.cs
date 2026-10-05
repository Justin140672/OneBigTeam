using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class ProgrammaticCompletionRecoveryTests
{
    private static ProgrammaticCompletionHarness NewHarness()
    {
        var dbName = Guid.NewGuid().ToString("N");
        return new ProgrammaticCompletionHarness(() =>
            new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(dbName).Options));
    }

    private static void FailPermanently(ProgrammaticCompletionHarness h) =>
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("Feedback outcome is required.")));

    [Fact]
    public async Task Terminal_Programmatic_State_Returns_An_Explicit_Terminal_Result_With_Diagnostic_Identifiers()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        FailPermanently(h);

        var first = await h.ResolveAsync();
        var second = await h.ResolveAsync();

        var state = await h.StateAsync(task.Id);
        foreach (var result in new[] { first, second })
        {
            Assert.Equal(TaskResolutionStatus.TerminalFailure, result.Status);
            Assert.Equal(task.Id, result.TaskId);
            Assert.Equal(state!.OperationId, result.OperationId);
            Assert.Contains("Feedback outcome is required", result.FailureReason);
            Assert.NotNull(result.TerminalFailureAt);
        }

        Assert.Equal(1, h.Action.Calls);
    }

    [Fact]
    public async Task Transient_Failure_Still_Throws_And_Outstanding_Is_Reported_For_A_Live_Lease()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Unexpected("downstream unavailable")));

        await Assert.ThrowsAsync<Services.TaskCompletionDispatchException>(() => h.ResolveAsync());

        h.Action.Behavior = _ => Task.FromResult(Result.Success());
        var confirmed = await h.ResolveAsync();

        Assert.Equal(TaskResolutionStatus.Confirmed, confirmed.Status);
        Assert.Equal(task.Id, confirmed.TaskId);
    }

    [Fact]
    public async Task Reset_Clears_Terminal_State_Keeps_Checkpoints_And_Resumes_Without_Repeating_Confirmed_Effects()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        FailPermanently(h);
        var terminal = await h.ResolveAsync();
        var before = await h.StateAsync(task.Id);
        Assert.NotNull(before!.AuditPublishedAt);
        Assert.NotNull(before.NotificationsClearedAt);
        Assert.Single(h.Audit.Published);

        var operatorUserId = Guid.NewGuid();
        var reset = await h.ResetAsync(terminal.OperationId!.Value, operatorUserId);

        Assert.Equal(TaskCompletionResetOutcome.Reset, reset.Outcome);
        var after = await h.StateAsync(task.Id);
        Assert.False(after!.IsTerminallyFailed);
        Assert.Null(after.FailureReason);
        Assert.Null(after.ClaimedBy);
        Assert.Null(after.ClaimedUntil);
        Assert.Equal(0, after.AttemptCount);
        Assert.Equal(1, after.ResetCount);
        Assert.Equal(operatorUserId, after.LastResetBy);
        Assert.Equal(before.AuditPublishedAt, after.AuditPublishedAt);
        Assert.Equal(before.NotificationsClearedAt, after.NotificationsClearedAt);
        Assert.Equal(before.OperationId, after.OperationId);

        h.Action.Behavior = _ => Task.FromResult(Result.Success());
        var resolved = await h.ResolveAsync();

        Assert.Equal(TaskResolutionStatus.Confirmed, resolved.Status);
        Assert.Equal(2, h.Action.Calls);
        Assert.Equal(1, h.Audit.Published.Count(e => e is TaskCompletedAuditEvent));
        Assert.NotNull((await h.StateAsync(task.Id))!.ConfirmedAt);
    }

    [Fact]
    public async Task Reset_Makes_The_Completion_Eligible_For_The_Reconciliation_Job_Immediately()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        FailPermanently(h);
        var terminal = await h.ResolveAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Success());

        await using (var db = h.NewDb())
            await h.NewJob(db).ExecuteAsync();
        Assert.Null((await h.StateAsync(task.Id))!.ConfirmedAt);

        await h.ResetAsync(terminal.OperationId!.Value);
        await using (var db = h.NewDb())
            await h.NewJob(db).ExecuteAsync();

        Assert.NotNull((await h.StateAsync(task.Id))!.ConfirmedAt);
    }

    [Fact]
    public async Task Repeated_Reset_Is_Idempotent_And_Does_Not_Start_Another_Cycle()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        FailPermanently(h);
        var terminal = await h.ResolveAsync();

        var first = await h.ResetAsync(terminal.OperationId!.Value);
        var second = await h.ResetAsync(terminal.OperationId!.Value);

        Assert.Equal(TaskCompletionResetOutcome.Reset, first.Outcome);
        Assert.Equal(TaskCompletionResetOutcome.NotTerminal, second.Outcome);
        Assert.Equal(1, (await h.StateAsync(task.Id))!.ResetCount);
        Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
    }

    [Fact]
    public async Task Reset_Publishes_An_Audit_Event_Identifying_Operator_Operation_And_Reason()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        FailPermanently(h);
        var terminal = await h.ResolveAsync();
        var operatorUserId = Guid.NewGuid();

        await h.ResetAsync(terminal.OperationId!.Value, operatorUserId, "Fixed the missing outcome data");

        var evt = Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
        Assert.Equal(operatorUserId, evt.OperatorUserId);
        Assert.Equal(terminal.OperationId, evt.OperationId);
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal("Fixed the missing outcome data", evt.Reason);
        Assert.Equal("task.completion_reset", ((IAuditEvent)evt).EventType);
        Assert.Equal(operatorUserId, ((IAuditEvent)evt).ActorUserId);
    }

    [Fact]
    public async Task Reset_Of_Unknown_Operation_Or_Other_Company_Returns_NotFound()
    {
        var h = NewHarness();
        await h.AddTaskAsync();
        FailPermanently(h);
        var terminal = await h.ResolveAsync();

        var unknown = await h.ResetAsync(Guid.NewGuid());
        await using var db = h.NewDb();
        var otherCompany = await h.NewRecovery(db).ResetTerminalCompletionAsync(
            Guid.NewGuid(), terminal.OperationId!.Value, Guid.NewGuid(), "x", CancellationToken.None);

        Assert.Equal(TaskCompletionResetOutcome.NotFound, unknown.Outcome);
        Assert.Equal(TaskCompletionResetOutcome.NotFound, otherCompany.Outcome);
        Assert.Empty(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
    }

    [Fact]
    public async Task Reset_Of_A_Confirmed_Completion_Changes_Nothing()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        var confirmed = await h.ResolveAsync();

        var reset = await h.ResetAsync(confirmed.OperationId!.Value);

        Assert.Equal(TaskCompletionResetOutcome.NotTerminal, reset.Outcome);
        Assert.Equal(0, (await h.StateAsync(task.Id))!.ResetCount);
    }
}

public class ProgrammaticCompletionRecoveryPostgresTests(TasksDatabaseFixture fixture) : IClassFixture<TasksDatabaseFixture>
{
    private ProgrammaticCompletionHarness NewHarness() => new(() => fixture.BuildContext());

    [Fact]
    public async Task Concurrent_Reset_Requests_Reset_Once_And_Are_Idempotent()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("permanent")));
        var terminal = await h.ResolveAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Task.Run(() => h.ResetAsync(terminal.OperationId!.Value))));

        Assert.Equal(1, results.Count(r => r.Outcome == TaskCompletionResetOutcome.Reset));
        Assert.All(results.Where(r => r.Outcome != TaskCompletionResetOutcome.Reset),
            r => Assert.Equal(TaskCompletionResetOutcome.NotTerminal, r.Outcome));
        var state = await h.StateAsync(task.Id);
        Assert.Equal(1, state!.ResetCount);
        Assert.False(state.IsTerminallyFailed);
        Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
    }

    [Fact]
    public async Task Reset_Racing_A_Stale_Writer_Reports_Conflict_Or_Idempotent_Success_Never_Corrupts_State()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("permanent")));
        var terminal = await h.ResolveAsync();

        await using var staleDb = fixture.BuildContext();
        var stale = await staleDb.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == task.Id);
        Assert.True(stale.IsTerminallyFailed);

        var winner = await h.ResetAsync(terminal.OperationId!.Value);
        Assert.Equal(TaskCompletionResetOutcome.Reset, winner.Outcome);

        var loser = await h.NewRecovery(staleDb).ResetTerminalCompletionAsync(
            h.CompanyId, terminal.OperationId!.Value, Guid.NewGuid(), "late", CancellationToken.None);

        Assert.Equal(TaskCompletionResetOutcome.NotTerminal, loser.Outcome);
        Assert.Equal(1, (await h.StateAsync(task.Id))!.ResetCount);
    }
}
