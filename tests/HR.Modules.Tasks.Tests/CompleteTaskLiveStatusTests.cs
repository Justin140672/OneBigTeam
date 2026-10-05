using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class CompleteTaskLiveStatusTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedNow);
    private static readonly FakeClock Clock = new(FixedNow);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private sealed class Rig
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

        public FakeAuditPublisher Audit { get; } = new();
        public FakeNotificationWriter Notifications { get; } = new();
        public ScriptedCompletionAction Action { get; } = new();
        public RecordingBackgroundJobClient JobClient { get; } = new();
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid TaskId { get; private set; }

        public TasksDbContext NewDb() =>
            new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(_dbName).Options);

        public CompleteTaskHandler Handler(TasksDbContext db, bool authorized = true) =>
            new(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                new TaskCompletionDispatcher([Action]),
                new TasksResourceAuthorizer(
                    authorized ? new FakeRoleAuthorizationService(HrAdministratorRoleId) : new FakeRoleAuthorizationService(),
                    new FakeDirectReportsReader()),
                JobClient, NullLogger<CompleteTaskHandler>.Instance);

        public async Task SeedTaskAsync()
        {
            await using var db = NewDb();
            var task = TaskItem.Create(
                Guid.NewGuid(), CompanyId, Guid.NewGuid(), "Record feedback", "private description", TaskPriority.Medium,
                TaskSource.Recruitment, TaskActionType.Complete, null, EmployeeId, null, Now,
                sourceEntityId: Guid.NewGuid());
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
            TaskId = task.Id;
        }

        public CompleteTaskRequest Request(string? key = "key-1", Guid? companyId = null, Guid? actor = null) =>
            new() { CompanyId = companyId ?? CompanyId, Id = TaskId, CompletedBy = actor ?? Guid.NewGuid(), IdempotencyKey = key };

        public async Task<Result<CompleteTaskResponse>> CompleteAsync(CompleteTaskRequest request, bool authorized = true)
        {
            await using var db = NewDb();
            return await Handler(db, authorized).HandleAsync(request, CancellationToken.None);
        }

        public async Task MutateOperationAsync(Action<TaskCompletionOperation> mutate)
        {
            await using var db = NewDb();
            var operation = await db.TaskCompletionOperations.SingleAsync(o => o.TaskId == TaskId);
            mutate(operation);
            await db.SaveChangesAsync();
        }

        public async Task<TaskCompletionOperation> OperationAsync()
        {
            await using var db = NewDb();
            return await db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.TaskId == TaskId);
        }

        public void AssertNothingRepeated()
        {
            Assert.Equal(1, Action.Calls);
            Assert.Single(Notifications.Written);
            Assert.Single(Audit.Published.OfType<TaskCompletedAuditEvent>());
        }
    }

    private static async Task<(Rig Rig, CompleteTaskRequest Request)> CompletedAsync()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var request = rig.Request();
        var first = await rig.CompleteAsync(request);
        Assert.True(first.IsSuccess);
        return (rig, request);
    }

    [Fact]
    public async Task Original_Request_With_Inline_Success_Reports_Confirmed()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();

        var result = await rig.CompleteAsync(rig.Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsConfirmed, result.Value!.EffectsStatus);
        Assert.Null(result.Value.ResolutionType);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.OperationAsync()).Status);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Original_Request_With_Inline_Failure_Reports_Pending_And_Queues_Recovery()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        rig.Audit.ThrowOnPublish = true;

        var result = await rig.CompleteAsync(rig.Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsPending, result.Value!.EffectsStatus);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, (await rig.OperationAsync()).Status);
        Assert.Single(rig.JobClient.CreatedJobs);
    }

    [Fact]
    public async Task Original_Request_Without_Key_Reports_Confirmed_Through_The_Same_Mapping()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();

        var result = await rig.CompleteAsync(rig.Request(key: null));

        Assert.Equal(CompletionStatusMapper.EffectsConfirmed, result.Value!.EffectsStatus);
    }

    [Fact]
    public async Task Replay_Of_A_Processed_Completion_Reports_Confirmed_Without_Repeating_Anything()
    {
        var (rig, request) = await CompletedAsync();

        var replay = await rig.CompleteAsync(request);

        Assert.True(replay.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsConfirmed, replay.Value!.EffectsStatus);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_Of_A_Dispatch_Applied_Completion_Reports_Pending_And_Does_Not_Enqueue()
    {
        var (rig, request) = await CompletedAsync();
        await rig.MutateOperationAsync(o => o.ResetEffectsTerminalFailure(Guid.NewGuid(), Now));

        var replay = await rig.CompleteAsync(request);

        Assert.True(replay.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsPending, replay.Value!.EffectsStatus);
        Assert.Empty(rig.JobClient.CreatedJobs);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_Of_An_Effects_Terminal_Failure_Is_The_Typed_Conflict_With_No_Sensitive_Text()
    {
        var (rig, request) = await CompletedAsync();
        await rig.MutateOperationAsync(o => o.MarkEffectsTerminalFailure(
            TaskCompletionOperation.CategoryEffectsRetryLimit, "secret exception text", Now));
        var operation = await rig.OperationAsync();

        var replay = await rig.CompleteAsync(request);
        var nonIdempotent = await rig.CompleteAsync(rig.Request(key: null));

        Assert.True(replay.IsFailure);
        Assert.Equal("conflict.effects_terminal_failure", replay.Error.Code);
        Assert.Equal(nonIdempotent.Error.Code, replay.Error.Code);
        Assert.Equal(operation.Id, replay.Error.Details!["operationId"]);
        Assert.Equal(rig.TaskId, replay.Error.Details["taskId"]);
        Assert.Equal(true, replay.Error.Details["resettable"]);
        Assert.Equal("reset", replay.Error.Details["recoveryAction"]);
        Assert.Equal(TaskCompletionOperation.CategoryEffectsRetryLimit, replay.Error.Details["failureCategory"]);
        var text = replay.Error.Message + string.Join(" ", replay.Error.Details.Values.Select(v => v?.ToString()));
        Assert.DoesNotContain("secret exception text", text);
        Assert.DoesNotContain("private description", text);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_Of_A_Data_Integrity_Failure_Is_The_Typed_Adjudication_Conflict()
    {
        var (rig, request) = await CompletedAsync();
        await rig.MutateOperationAsync(o => o.MarkDataIntegrityFailure("secret exception text", Now));

        var replay = await rig.CompleteAsync(request);

        Assert.True(replay.IsFailure);
        Assert.Equal("conflict.data_integrity_failure", replay.Error.Code);
        Assert.Equal(false, replay.Error.Details!["resettable"]);
        Assert.Equal("adjudicate", replay.Error.Details["recoveryAction"]);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_After_Reset_And_Reprocessing_Reflects_The_New_Live_State()
    {
        var (rig, request) = await CompletedAsync();
        await rig.MutateOperationAsync(o => o.MarkEffectsTerminalFailure(
            TaskCompletionOperation.CategoryEffectsRetryLimit, "x", Now));
        Assert.True((await rig.CompleteAsync(request)).IsFailure);

        await rig.MutateOperationAsync(o => o.ResetEffectsTerminalFailure(Guid.NewGuid(), Now));
        var afterReset = await rig.CompleteAsync(request);
        Assert.Equal(CompletionStatusMapper.EffectsPending, afterReset.Value!.EffectsStatus);

        await rig.MutateOperationAsync(o => o.MarkProcessed(Now));
        var afterProcessed = await rig.CompleteAsync(request);
        Assert.Equal(CompletionStatusMapper.EffectsConfirmed, afterProcessed.Value!.EffectsStatus);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_After_Effects_Verification_Reports_Verified()
    {
        var (rig, request) = await CompletedAsync();
        await rig.MutateOperationAsync(o =>
        {
            o.MarkDataIntegrityFailure("x", Now);
            o.MarkEffectsVerified(Guid.NewGuid(), Now);
        });

        var replay = await rig.CompleteAsync(request);

        Assert.True(replay.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsVerified, replay.Value!.EffectsStatus);
        Assert.Equal(CompletionStatusMapper.ResolutionVerified, replay.Value.ResolutionType);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_After_Waiver_Reports_Waived_Never_Confirmed()
    {
        var (rig, request) = await CompletedAsync();
        await rig.MutateOperationAsync(o =>
        {
            o.MarkDataIntegrityFailure("x", Now);
            o.MarkWaived(Guid.NewGuid(), Now);
        });

        var replay = await rig.CompleteAsync(request);

        Assert.True(replay.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsWaived, replay.Value!.EffectsStatus);
        Assert.Equal(CompletionStatusMapper.ResolutionWaived, replay.Value.ResolutionType);
        Assert.NotEqual(CompletionStatusMapper.EffectsConfirmed, replay.Value.EffectsStatus);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Key_Reuse_With_A_Different_Fingerprint_Is_Still_Rejected()
    {
        var (rig, request) = await CompletedAsync();

        var reused = await rig.CompleteAsync(request with { OutcomeDecision = "approve" });

        Assert.True(reused.IsFailure);
        Assert.Equal("conflict", reused.Error.Code);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Unauthorized_Caller_Cannot_Receive_The_Stored_Response()
    {
        var (rig, request) = await CompletedAsync();

        var replay = await rig.CompleteAsync(request, authorized: false);

        Assert.True(replay.IsFailure);
        Assert.Equal("forbidden", replay.Error.Code);
    }

    [Fact]
    public async Task Caller_Cannot_Replay_An_Operation_From_Another_Company()
    {
        var (rig, request) = await CompletedAsync();

        var replay = await rig.CompleteAsync(request with { CompanyId = Guid.NewGuid() });

        Assert.True(replay.IsFailure);
        Assert.Equal("not_found", replay.Error.Code);
        rig.AssertNothingRepeated();
    }

    [Fact]
    public async Task Replay_Of_A_Legacy_Completion_Without_An_Operation_Reports_No_Effects_Status()
    {
        var (rig, request) = await CompletedAsync();
        await using (var db = rig.NewDb())
        {
            db.TaskCompletionOperations.RemoveRange(db.TaskCompletionOperations);
            await db.SaveChangesAsync();
        }

        var replay = await rig.CompleteAsync(request);

        Assert.True(replay.IsSuccess);
        Assert.Null(replay.Value!.EffectsStatus);
        Assert.Equal(1, rig.Action.Calls);
    }
}
