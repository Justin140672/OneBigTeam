using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class CompleteTaskCanonicalCommandTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedNow);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private sealed class Rig
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

        public FakeClock Clock { get; } = new(FixedNow);
        public FakeAuditPublisher Audit { get; } = new();
        public FakeNotificationWriter Notifications { get; } = new();
        public ScriptedCompletionAction Action { get; } = new();
        public RecordingBackgroundJobClient JobClient { get; } = new();
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid TaskId { get; private set; }

        public TasksDbContext NewDb() =>
            new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(_dbName).Options);

        public async Task SeedTaskAsync(TaskSource source = TaskSource.Recruitment, TaskActionType actionType = TaskActionType.Complete)
        {
            await using var db = NewDb();
            var task = TaskItem.Create(
                Guid.NewGuid(), CompanyId, Guid.NewGuid(), "Record feedback", null, TaskPriority.Medium,
                source, actionType, null, Guid.NewGuid(), null, Now,
                sourceEntityId: Guid.NewGuid());
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
            TaskId = task.Id;
        }

        public CompleteTaskRequest Request(string? decision, string? reason, string? key = null) => new()
        {
            CompanyId = CompanyId, Id = TaskId, CompletedBy = Guid.NewGuid(), IdempotencyKey = key,
            OutcomeDecision = decision, OutcomeReason = reason,
        };

        public async Task<Result<CompleteTaskResponse>> CompleteAsync(CompleteTaskRequest request, params ITaskCompletionAction[] actions)
        {
            await using var db = NewDb();
            var handler = new CompleteTaskHandler(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                new TaskCompletionDispatcher(actions.Length > 0 ? actions : [Action]),
                new TasksResourceAuthorizer(
                    new FakeRoleAuthorizationService(HrAdministratorRoleId),
                    new FakeDirectReportsReader()),
                JobClient, NullLogger<CompleteTaskHandler>.Instance);
            return await handler.HandleAsync(request, CancellationToken.None);
        }

        public async Task<List<TaskCompletionOperation>> OperationsAsync()
        {
            await using var db = NewDb();
            return await db.TaskCompletionOperations.AsNoTracking().Where(o => o.TaskId == TaskId).ToListAsync();
        }

        public async Task SeedLegacyPendingAsync(string? decision, string? reason)
        {
            await using var db = NewDb();
            var operation = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), CompanyId, TaskId, Guid.NewGuid(), "x", null, Now - TimeSpan.FromMinutes(10));
            db.TaskCompletionOperations.Add(operation);
            var entry = db.Entry(operation);
            entry.Property(o => o.OutcomeDecision).CurrentValue = decision;
            entry.Property(o => o.OutcomeReason).CurrentValue = reason;
            entry.Property(o => o.CommandFingerprint).CurrentValue = null;
            await db.SaveChangesAsync();
        }
    }

    // ── Domain ────────────────────────────────────────────────────────────────

    [Fact]
    public void CreatePending_Persists_Canonical_Values_And_Matching_Fingerprint()
    {
        var taskId = Guid.NewGuid();

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), taskId, Guid.NewGuid(), "  Approve ", "  needs  two spaces  ", Now);

        Assert.Equal("Approve", operation.OutcomeDecision);
        Assert.Equal("needs  two spaces", operation.OutcomeReason);
        Assert.Equal(
            TaskCompletionCommand.Create(taskId, operation.OutcomeDecision, operation.OutcomeReason).Fingerprint(),
            operation.CommandFingerprint);
        Assert.Equal(operation.ToCommand().Fingerprint(), operation.CommandFingerprint);
    }

    [Fact]
    public void CreatePending_From_Command_Derives_Every_Field_From_The_Command()
    {
        var command = TaskCompletionCommand.Create(Guid.NewGuid(), " Reject ", " why ");

        var operation = TaskCompletionOperation.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), command, Now);

        Assert.Equal(command.TaskId, operation.TaskId);
        Assert.Equal(command.Decision, operation.OutcomeDecision);
        Assert.Equal(command.Reason, operation.OutcomeReason);
        Assert.Equal(command.Fingerprint(), operation.CommandFingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Blank_Decision_And_Reason_Normalize_To_Null_Consistently(string? blank)
    {
        var command = TaskCompletionCommand.Create(Guid.NewGuid(), blank, blank);

        Assert.Null(command.Decision);
        Assert.Null(command.Reason);
        Assert.Equal(TaskCompletionCommand.Create(command.TaskId, null, null).Fingerprint(), command.Fingerprint());
    }

    [Fact]
    public void Normalization_Trims_Surrounding_Whitespace_And_Preserves_Inner_Text_And_Case()
    {
        var command = TaskCompletionCommand.Create(Guid.NewGuid(), "\t Approve Now \n", "  Mixed  CASE\treason ");

        Assert.Equal("Approve Now", command.Decision);
        Assert.Equal("Mixed  CASE\treason", command.Reason);
    }

    [Fact]
    public void Changing_Any_Canonical_Field_Changes_The_Fingerprint()
    {
        var taskId = Guid.NewGuid();
        var baseline = TaskCompletionCommand.Create(taskId, "Approve", "ok").Fingerprint();

        Assert.NotEqual(baseline, TaskCompletionCommand.Create(Guid.NewGuid(), "Approve", "ok").Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(taskId, "approve", "ok").Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(taskId, "Approve", "o  k").Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(taskId, "Approve", null).Fingerprint());
    }

    [Fact]
    public void Validate_Checks_Max_Length_After_Normalization()
    {
        var taskId = Guid.NewGuid();
        var paddedAtLimit = " " + new string('r', TaskCompletionCommand.MaxReasonLength) + " ";

        Assert.Null(TaskCompletionCommand.Create(taskId, "Approve", paddedAtLimit).Validate());
        Assert.Equal(TaskCompletionCommand.InvalidCommandCode,
            TaskCompletionCommand.Create(taskId, "Approve", new string('r', TaskCompletionCommand.MaxReasonLength + 1)).Validate()!.Code);
        Assert.Equal(TaskCompletionCommand.InvalidCommandCode,
            TaskCompletionCommand.Create(taskId, new string('d', TaskCompletionCommand.MaxDecisionLength + 1), null).Validate()!.Code);
    }

    // ── Action ────────────────────────────────────────────────────────────────

    private static TaskCompletionContext LeaveContext(string? decision, string? reason) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Review leave", null, TaskSource.Leave, TaskActionType.Approve,
        null, Guid.NewGuid(), Now, Guid.NewGuid(), decision, reason, Guid.NewGuid());

    [Theory]
    [InlineData("Approve", "Approve")]
    [InlineData(" Approve ", "Approve")]
    [InlineData("Reject", "Reject")]
    [InlineData("\tReject\n", "Reject")]
    public async Task Leave_Action_Accepts_Canonical_And_Surrounding_Whitespace_Decisions_Identically(string raw, string expected)
    {
        var canonical = TaskCompletionCommand.Create(Guid.NewGuid(), raw, " because ");
        var service = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(service);

        var result = await action.ExecuteAsync(LeaveContext(canonical.Decision, canonical.Reason), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(service.Calls);
        Assert.Equal(expected, call.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Leave_Action_Rejects_A_Blank_Decision_As_Missing(string raw)
    {
        var canonical = TaskCompletionCommand.Create(Guid.NewGuid(), raw, null);
        var service = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(service);

        var result = await action.ExecuteAsync(LeaveContext(canonical.Decision, canonical.Reason), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(" Maybe ")]
    public async Task Invalid_Leave_Decision_Rejects_The_Operation_Leaves_The_Task_Open_And_Calls_No_Service(string raw)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync(TaskSource.Leave, TaskActionType.Approve);
        var service = new FakeLeaveApprovalService();

        var result = await rig.CompleteAsync(rig.Request(raw, null, "k"), new LeaveTaskCompletionAction(service));

        Assert.True(result.IsFailure);
        Assert.Empty(service.Calls);
        var operation = Assert.Single(await rig.OperationsAsync());
        Assert.Equal(TaskCompletionOperation.StatusRejected, operation.Status);
        await using var db = rig.NewDb();
        var task = await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == rig.TaskId);
        Assert.Equal(TaskItemStatus.Open, task.Status);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("Maybe")]
    [InlineData("Appr ove")]
    public async Task Leave_Action_Still_Rejects_Different_Or_Invalid_Canonical_Decisions(string raw)
    {
        var canonical = TaskCompletionCommand.Create(Guid.NewGuid(), $" {raw} ", null);
        var service = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(service);

        var result = await action.ExecuteAsync(LeaveContext(canonical.Decision, canonical.Reason), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(service.Calls);
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(" Approve ", "Approve", "  fine  ", "fine")]
    [InlineData("Approve", "Approve", "fine", "fine")]
    [InlineData("\tApprove\n", "Approve", "   ", null)]
    public async Task Handler_Persists_And_Dispatches_Only_Canonical_Values(
        string rawDecision, string decision, string? rawReason, string? reason)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();

        var result = await rig.CompleteAsync(rig.Request(rawDecision, rawReason, "k"));

        Assert.True(result.IsSuccess);
        var context = Assert.Single(rig.Action.Contexts);
        Assert.Equal(decision, context.OutcomeDecision);
        Assert.Equal(reason, context.OutcomeReason);
        var operation = Assert.Single(await rig.OperationsAsync());
        Assert.Equal(decision, operation.OutcomeDecision);
        Assert.Equal(reason, operation.OutcomeReason);
        Assert.Equal(operation.ToCommand().Fingerprint(), operation.CommandFingerprint);
    }

    [Fact]
    public async Task Whitespace_Equivalent_Request_Against_A_Live_Claim_Converges_Without_Dispatch()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        TaskCompletionOperation seeded;
        await using (var db = rig.NewDb())
        {
            seeded = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), rig.CompanyId, rig.TaskId, Guid.NewGuid(), " Approve ", "  why ", Now);
            seeded.Claim(Guid.NewGuid(), Now);
            db.TaskCompletionOperations.Add(seeded);
            await db.SaveChangesAsync();
        }

        var equivalent = await rig.CompleteAsync(rig.Request("Approve", "why"));
        var different = await rig.CompleteAsync(rig.Request("Reject", "why"));

        Assert.True(equivalent.IsSuccess);
        Assert.Equal(CompletionStatusMapper.CommandMismatchCode, different.Error.Code);
        Assert.Equal(0, rig.Action.Calls);
        var after = Assert.Single(await rig.OperationsAsync());
        Assert.Equal(seeded.Id, after.Id);
        Assert.Equal(seeded.CompletedBy, after.CompletedBy);
        Assert.Equal("Approve", after.OutcomeDecision);
        Assert.Equal("why", after.OutcomeReason);
    }

    [Fact]
    public async Task Materially_Different_Reason_Is_A_Command_Mismatch()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        Assert.True((await rig.CompleteAsync(rig.Request("Approve", "why"))).IsSuccess);

        var result = await rig.CompleteAsync(rig.Request("Approve", "w hy"));

        Assert.Equal(CompletionStatusMapper.CommandMismatchCode, result.Error.Code);
        Assert.Equal(1, rig.Action.Calls);
    }

    [Fact]
    public async Task Invalid_Canonical_Command_Is_Rejected_Before_An_Operation_Is_Created()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();

        var result = await rig.CompleteAsync(rig.Request(" Approve ", " " + new string('r', TaskCompletionCommand.MaxReasonLength + 1) + " ", "k"));

        Assert.True(result.IsFailure);
        Assert.Equal(TaskCompletionCommand.InvalidCommandCode, result.Error.Code);
        Assert.Empty(await rig.OperationsAsync());
        Assert.Equal(0, rig.Action.Calls);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Same_Key_With_Only_Whitespace_Differences_Replays_And_Dispatches_Once()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();

        var first = await rig.CompleteAsync(rig.Request("Approve", "why", "key-1"));
        var repeat = await rig.CompleteAsync(rig.Request("  Approve ", " why\t", "key-1"));

        Assert.True(first.IsSuccess);
        Assert.True(repeat.IsSuccess);
        Assert.Equal(1, rig.Action.Calls);
        Assert.Single(await rig.OperationsAsync());
    }

    [Fact]
    public async Task Same_Key_With_A_Different_Decision_Is_A_Key_Reuse_Conflict()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        Assert.True((await rig.CompleteAsync(rig.Request("Approve", "why", "key-1"))).IsSuccess);

        var different = await rig.CompleteAsync(rig.Request("Reject", "why", "key-1"));
        var differentReason = await rig.CompleteAsync(rig.Request("Approve", "other", "key-1"));

        Assert.Equal("conflict", different.Error.Code);
        Assert.Contains("Idempotency-Key", different.Error.Message);
        Assert.Equal("conflict", differentReason.Error.Code);
        Assert.Equal(1, rig.Action.Calls);
    }

    [Fact]
    public async Task Different_Key_With_A_Different_Decision_Is_A_Command_Mismatch()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        Assert.True((await rig.CompleteAsync(rig.Request("Approve", "why", "key-1"))).IsSuccess);

        var result = await rig.CompleteAsync(rig.Request("Reject", "why", "key-2"));

        Assert.Equal(CompletionStatusMapper.CommandMismatchCode, result.Error.Code);
        Assert.Equal(1, rig.Action.Calls);
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Reconciliation_Dispatches_Canonical_Values_For_A_Legacy_Row_And_Preserves_Identity()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        await rig.SeedLegacyPendingAsync("  Approve ", "   ");
        var legacy = Assert.Single(await rig.OperationsAsync());
        Assert.Null(legacy.CommandFingerprint);

        await using (var db = rig.NewDb())
        {
            var job = new TaskCompletionReconciliationJob(db, new TaskCompletionDispatcher([rig.Action]),
                rig.Clock, rig.JobClient, NullLogger<TaskCompletionReconciliationJob>.Instance);
            await job.ExecuteAsync();
        }

        var context = Assert.Single(rig.Action.Contexts);
        Assert.Equal("Approve", context.OutcomeDecision);
        Assert.Null(context.OutcomeReason);
        Assert.Equal(legacy.Id, context.DispatchOperationId);

        var equivalent = await rig.CompleteAsync(rig.Request("Approve", null));
        Assert.True(equivalent.IsSuccess);
        Assert.Equal(1, rig.Action.Calls);
        var after = Assert.Single(await rig.OperationsAsync());
        Assert.Equal(legacy.Id, after.Id);
        Assert.Equal("  Approve ", after.OutcomeDecision);
    }
}
