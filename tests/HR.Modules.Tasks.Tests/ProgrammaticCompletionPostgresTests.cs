using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Tasks.Tests;

public class ProgrammaticCompletionPostgresTests(TasksDatabaseFixture fixture) : IClassFixture<TasksDatabaseFixture>
{
    private ProgrammaticCompletionHarness NewHarness() => new(() => fixture.BuildContext());

    private sealed class BeforeFirstSaveInterceptor(Func<Task> beforeFirstSave) : SaveChangesInterceptor
    {
        private int _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                await beforeFirstSave();

            return result;
        }
    }

    [Fact]
    public async Task Two_Completers_Racing_To_Create_State_For_The_Same_Open_Task_Run_Effects_Once()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => h.CompleteAsync())));

        Assert.Contains(true, results);
        Assert.Equal(1, await h.StateCountAsync());
        Assert.Equal(1, h.Action.Calls);
        Assert.Single(h.Audit.Published);
        Assert.Equal(TaskItemStatus.Completed, await h.TaskStatusAsync(task.Id));

        Assert.True(await h.CompleteAsync());
        Assert.Equal(1, h.Action.Calls);
    }

    [Fact]
    public async Task Creation_Race_Loser_Reloads_The_Confirmed_Winner_Without_An_Unhandled_Error()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();

        var interceptor = new BeforeFirstSaveInterceptor(async () => Assert.True(await h.CompleteAsync()));
        await using var loserDb = fixture.BuildContext(interceptor);

        var loserResult = await h.CompleteAsync(loserDb);

        Assert.True(loserResult);
        Assert.Equal(1, await h.StateCountAsync());
        Assert.Equal(1, h.Action.Calls);
        Assert.Single(h.Audit.Published);
        Assert.NotNull((await h.StateAsync(task.Id))!.ConfirmedAt);
    }

    [Fact]
    public async Task Creation_Race_Loser_Reports_Outstanding_While_The_Winner_Holds_A_Live_Lease()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();

        var interceptor = new BeforeFirstSaveInterceptor(async () =>
        {
            await using var winnerDb = fixture.BuildContext();
            var winnerTask = await winnerDb.TaskItems.SingleAsync(t => t.Id == task.Id);
            var winner = ProgrammaticTaskCompletion.Create(task.Id, h.CompanyId, Guid.NewGuid(), "Open", ProgrammaticCompletionHarness.Now);
            winner.Claim(Guid.NewGuid(), ProgrammaticCompletionHarness.Now);
            winnerTask.Complete(winner.CompletedBy, ProgrammaticCompletionHarness.Now);
            winnerDb.ProgrammaticTaskCompletions.Add(winner);
            await winnerDb.SaveChangesAsync();
        });
        await using var loserDb = fixture.BuildContext(interceptor);

        var loserResult = await h.CompleteAsync(loserDb);

        Assert.False(loserResult);
        Assert.Equal(1, await h.StateCountAsync());
        Assert.Equal(0, h.Action.Calls);
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task Two_Workers_Racing_To_Process_The_Same_Unconfirmed_State_Execute_Audit_And_Dispatch_Once()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        await h.AddUnconfirmedStateAsync(task);
        h.Action.Behavior = async _ =>
        {
            await Task.Delay(300);
            return Result.Success();
        };

        await using var dbA = h.NewDb();
        await using var dbB = h.NewDb();
        var taskA = await dbA.TaskItems.SingleAsync(t => t.Id == task.Id);
        var stateA = await dbA.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == task.Id);
        var taskB = await dbB.TaskItems.SingleAsync(t => t.Id == task.Id);
        var stateB = await dbB.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == task.Id);

        var results = await Task.WhenAll(
            Task.Run(() => h.NewCompleter(dbA).ReconcileAsync(taskA, stateA, CancellationToken.None)),
            Task.Run(() => h.NewCompleter(dbB).ReconcileAsync(taskB, stateB, CancellationToken.None)));

        Assert.Single(results, r => r);
        Assert.Equal(1, h.Action.Calls);
        Assert.Single(h.Audit.Published);
        Assert.NotNull((await h.StateAsync(task.Id))!.ConfirmedAt);
    }

    [Fact]
    public async Task Expired_Lease_Is_Reclaimed_And_Processed()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        var staleClaim = await h.AddUnconfirmedStateAsync(
            task, ProgrammaticCompletionHarness.Now - ProgrammaticTaskCompletion.LeaseDuration - TimeSpan.FromMinutes(1));

        await using var db = h.NewDb();
        await h.NewJob(db).ExecuteAsync();

        var state = await h.StateAsync(task.Id);
        Assert.NotNull(state!.ConfirmedAt);
        Assert.Null(state.ClaimedBy);
        Assert.Equal(staleClaim.OperationId, state.OperationId);
        Assert.Equal(1, h.Action.Calls);
    }

    [Fact]
    public async Task Live_Lease_Cannot_Be_Stolen()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        var claimed = await h.AddUnconfirmedStateAsync(task, ProgrammaticCompletionHarness.Now - TimeSpan.FromMinutes(1));

        await using var db = h.NewDb();
        var tracked = await db.TaskItems.SingleAsync(t => t.Id == task.Id);
        var state = await db.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == task.Id);

        Assert.False(await h.NewCompleter(db).ReconcileAsync(tracked, state, CancellationToken.None));
        Assert.False(await h.CompleteAsync());

        var after = await h.StateAsync(task.Id);
        Assert.Equal(claimed.ClaimedBy, after!.ClaimedBy);
        Assert.Null(after.ConfirmedAt);
        Assert.Equal(0, h.Action.Calls);
        Assert.Empty(h.Audit.Published);

        await using var jobDb = h.NewDb();
        await h.NewJob(jobDb).ExecuteAsync();
        Assert.Equal(0, h.Action.Calls);
    }

    [Fact]
    public async Task Stale_Version_Claim_Loses_Safely_Without_Running_Effects()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        await h.AddUnconfirmedStateAsync(task);

        await using var staleDb = h.NewDb();
        var staleTask = await staleDb.TaskItems.SingleAsync(t => t.Id == task.Id);
        var staleState = await staleDb.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == task.Id);

        Assert.True(await h.CompleteAsync());
        Assert.Equal(1, h.Action.Calls);

        Assert.False(await h.NewCompleter(staleDb).ReconcileAsync(staleTask, staleState, CancellationToken.None));

        Assert.Equal(1, h.Action.Calls);
        Assert.Single(h.Audit.Published);
    }

    [Fact]
    public async Task Worker_That_Loses_Its_Lease_Mid_Run_Stops_Without_Overwriting_The_Newer_Owner()
    {
        var h = NewHarness();
        var task = await h.AddTaskAsync();
        await h.AddUnconfirmedStateAsync(task);
        h.Action.Behavior = async _ =>
        {
            await using var other = h.NewDb();
            await other.Database.ExecuteSqlAsync(
                $"UPDATE tasks.programmatic_task_completions SET version = version + 1, claimed_by = {Guid.NewGuid()} WHERE task_id = {task.Id}");
            return Result.Success();
        };

        await using var db = h.NewDb();
        var tracked = await db.TaskItems.SingleAsync(t => t.Id == task.Id);
        var state = await db.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == task.Id);

        var confirmed = await h.NewCompleter(db).ReconcileAsync(tracked, state, CancellationToken.None);

        Assert.False(confirmed);
        var after = await h.StateAsync(task.Id);
        Assert.Null(after!.ConfirmedAt);
        Assert.Null(after.DispatchedAt);
        Assert.Null(after.FailureReason);
    }
}
