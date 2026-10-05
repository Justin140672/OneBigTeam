using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class TaskRecoveryAuditTests
{
    private sealed class CancellingAudit(CancellationTokenSource cts) : IAuditEventPublisher, IAuditEventExistenceReader
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private static ProgrammaticCompletionHarness NewHarness()
    {
        var dbName = Guid.NewGuid().ToString("N");
        return new ProgrammaticCompletionHarness(() =>
            new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(dbName).Options));
    }

    private static async Task<(Guid OperationId, Guid TaskId)> TerminalProgrammaticAsync(ProgrammaticCompletionHarness h)
    {
        var task = await h.AddTaskAsync();
        h.Action.Behavior = _ => Task.FromResult(Result.Failure(Error.Validation("Feedback outcome is required.")));
        var terminal = await h.ResolveAsync();
        Assert.Equal(TaskResolutionStatus.TerminalFailure, terminal.Status);
        return (terminal.OperationId!.Value, task.Id);
    }

    private static async Task<List<TaskRecoveryAction>> ActionsAsync(ProgrammaticCompletionHarness h)
    {
        await using var db = h.NewDb();
        return await db.TaskRecoveryActions.AsNoTracking().OrderBy(a => a.SequenceNumber).ToListAsync();
    }

    [Fact]
    public async Task Reset_Saves_A_Durable_Intent_With_The_Stable_Event_Id_And_Delivers_The_Same_Id()
    {
        var h = NewHarness();
        var (operationId, taskId) = await TerminalProgrammaticAsync(h);

        var reset = await h.ResetAsync(operationId);
        Assert.Equal(TaskCompletionResetOutcome.Reset, reset.Outcome);

        var action = Assert.Single(await ActionsAsync(h));
        Assert.Equal(TaskRecoveryAction.EventIdFor(operationId, 1), action.Id);
        Assert.Equal(reset.RecoveryActionId, action.Id);
        Assert.Equal(h.CompanyId, action.CompanyId);
        Assert.Equal(taskId, action.TaskId);
        Assert.Equal(operationId, action.OperationId);
        Assert.Equal(1, action.SequenceNumber);
        Assert.NotNull(action.AuditDeliveredAt);

        var evt = Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
        Assert.Equal(action.Id, ((IAuditEvent)evt).EventId);
    }

    [Fact]
    public async Task Reset_Succeeds_While_Audit_Persistence_Is_Unavailable_And_The_Sweep_Later_Delivers_Without_Repeating_The_Reset()
    {
        var h = NewHarness();
        var (operationId, taskId) = await TerminalProgrammaticAsync(h);
        h.Audit.SwallowPersistenceFailure = true;

        var reset = await h.ResetAsync(operationId);

        Assert.Equal(TaskCompletionResetOutcome.Reset, reset.Outcome);
        var pending = Assert.Single(await ActionsAsync(h));
        Assert.Null(pending.AuditDeliveredAt);
        Assert.Equal(1, pending.AuditAttemptCount);
        Assert.NotNull(pending.LastAuditFailure);
        Assert.Empty(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());

        h.Audit.SwallowPersistenceFailure = false;
        await using (var db = h.NewDb())
            Assert.Equal(1, await h.NewAuditDelivery(db).DeliverOutstandingAsync(CancellationToken.None));

        var delivered = Assert.Single(await ActionsAsync(h));
        Assert.NotNull(delivered.AuditDeliveredAt);
        Assert.Equal(pending.Id, ((IAuditEvent)Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>())).EventId);
        Assert.Equal(1, (await h.StateAsync(taskId))!.ResetCount);
    }

    [Fact]
    public async Task Repeated_Delivery_Of_One_Action_Creates_Exactly_One_Audit_Event()
    {
        var h = NewHarness();
        var (operationId, _) = await TerminalProgrammaticAsync(h);
        h.Audit.SwallowPersistenceFailure = true;
        await h.ResetAsync(operationId);
        h.Audit.SwallowPersistenceFailure = false;

        for (var i = 0; i < 3; i++)
        {
            await using var db = h.NewDb();
            await h.NewAuditDelivery(db).DeliverOutstandingAsync(CancellationToken.None);
        }

        Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>());
    }

    [Fact]
    public async Task Delivery_After_A_Crash_Following_Publish_Marks_Delivered_Without_Publishing_Again()
    {
        var h = NewHarness();
        var (operationId, taskId) = await TerminalProgrammaticAsync(h);
        h.Audit.ThrowOnPublish = true;
        await h.ResetAsync(operationId);
        Assert.Equal(0, h.Audit.Published.Count(e => e is ProgrammaticTaskCompletionResetAuditEvent));

        h.Audit.ThrowOnPublish = false;
        h.Audit.Seed(new ProgrammaticTaskCompletionResetAuditEvent(
            h.CompanyId, taskId, operationId, Guid.NewGuid(), "r", 1, ProgrammaticCompletionHarness.Now));
        var publishCallsBefore = h.Audit.PublishCalls;

        await using (var db = h.NewDb())
            await h.NewAuditDelivery(db).DeliverOutstandingAsync(CancellationToken.None);

        Assert.Equal(publishCallsBefore, h.Audit.PublishCalls);
        Assert.NotNull(Assert.Single(await ActionsAsync(h)).AuditDeliveredAt);
    }

    [Fact]
    public async Task Two_Legitimate_Resets_Create_Two_Distinct_Audit_Events()
    {
        var h = NewHarness();
        var (operationId, taskId) = await TerminalProgrammaticAsync(h);
        await h.ResetAsync(operationId);

        var second = await h.ResolveAsync();
        Assert.Equal(TaskResolutionStatus.TerminalFailure, second.Status);
        await h.ResetAsync(operationId);

        var actions = await ActionsAsync(h);
        Assert.Equal([1, 2], actions.Select(a => a.SequenceNumber));
        Assert.NotEqual(actions[0].Id, actions[1].Id);
        Assert.All(actions, a => Assert.NotNull(a.AuditDeliveredAt));
        var eventIds = h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>()
            .Select(e => ((IAuditEvent)e).EventId).ToList();
        Assert.Equal(2, eventIds.Distinct().Count());
        Assert.Equal(2, (await h.StateAsync(taskId))!.ResetCount);
    }

    [Fact]
    public async Task Pending_And_Committed_Rows_Share_The_Same_Event_Id()
    {
        var h = NewHarness();
        var (operationId, _) = await TerminalProgrammaticAsync(h);
        h.Audit.SwallowPersistenceFailure = true;
        await h.ResetAsync(operationId);
        var pendingId = Assert.Single(await ActionsAsync(h)).Id;

        h.Audit.SwallowPersistenceFailure = false;
        await using (var db = h.NewDb())
            await h.NewAuditDelivery(db).DeliverOutstandingAsync(CancellationToken.None);

        var committedEventId = ((IAuditEvent)Assert.Single(h.Audit.Published.OfType<ProgrammaticTaskCompletionResetAuditEvent>())).EventId;
        Assert.Equal(pendingId, committedEventId);
        Assert.Equal(pendingId, TaskRecoveryAction.EventIdFor(operationId, 1));
    }

    [Fact]
    public async Task Request_Cancellation_After_The_State_Save_Does_Not_Lose_The_Intent()
    {
        var h = NewHarness();
        var (operationId, _) = await TerminalProgrammaticAsync(h);
        using var cts = new CancellationTokenSource();
        var audit = new CancellingAudit(cts);

        await using (var db = h.NewDb())
        {
            var delivery = new TaskRecoveryAuditDelivery(
                db, audit, audit, new FakeClock(ProgrammaticCompletionHarness.FixedUtcNow),
                NullLogger<TaskRecoveryAuditDelivery>.Instance);
            var recovery = new TaskCompletionRecovery(
                db, new FakeClock(ProgrammaticCompletionHarness.FixedUtcNow), delivery,
                NullLogger<TaskCompletionRecovery>.Instance);

            var reset = await recovery.ResetTerminalCompletionAsync(
                h.CompanyId, operationId, Guid.NewGuid(), "reason", cts.Token);

            Assert.Equal(TaskCompletionResetOutcome.Reset, reset.Outcome);
        }

        var action = Assert.Single(await ActionsAsync(h));
        Assert.Null(action.AuditDeliveredAt);
        Assert.Equal(1, action.AuditAttemptCount);
    }

    [Fact]
    public async Task Reset_Of_Another_Company_Operation_Is_NotFound_And_Creates_No_Intent()
    {
        var h = NewHarness();
        var (operationId, _) = await TerminalProgrammaticAsync(h);

        await using var db = h.NewDb();
        var result = await h.NewRecovery(db).ResetTerminalCompletionAsync(
            Guid.NewGuid(), operationId, Guid.NewGuid(), "reason", CancellationToken.None);

        Assert.Equal(TaskCompletionResetOutcome.NotFound, result.Outcome);
        Assert.Empty(await ActionsAsync(h));
    }

    [Fact]
    public async Task State_Reader_Reports_Reset_State_And_Hides_Other_Companies()
    {
        var h = NewHarness();
        var (operationId, _) = await TerminalProgrammaticAsync(h);
        var operatorId = Guid.NewGuid();

        await using var db = h.NewDb();
        var reader = new TaskCompletionOperationStateReader(db);

        var terminal = await reader.GetAsync(h.CompanyId, operationId, CancellationToken.None);
        Assert.True(terminal!.IsTerminal);
        Assert.Equal(0, terminal.ResetCount);

        await h.NewRecovery(db).ResetTerminalCompletionAsync(
            h.CompanyId, operationId, operatorId, "reason", CancellationToken.None);

        var afterReset = await reader.GetAsync(h.CompanyId, operationId, CancellationToken.None);
        Assert.False(afterReset!.IsTerminal);
        Assert.Equal(1, afterReset.ResetCount);
        Assert.Equal(operatorId, afterReset.LastResetBy);
        Assert.Null(await reader.GetAsync(Guid.NewGuid(), operationId, CancellationToken.None));
    }
}
