using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class CompleteTaskClaimTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedNow);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private sealed class BeforeFirstSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await beforeSave();
            }

            return result;
        }
    }

    private sealed class Rig
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

        public string DbName => _dbName;

        public FakeClock Clock { get; } = new(FixedNow);
        public FakeAuditPublisher Audit { get; } = new();
        public FakeNotificationWriter Notifications { get; } = new();
        public ScriptedCompletionAction Action { get; } = new();
        public RecordingBackgroundJobClient JobClient { get; } = new();
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid TaskId { get; private set; }

        public TasksDbContext NewDb() =>
            new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(_dbName).Options);

        public CompleteTaskHandler Handler(TasksDbContext db) =>
            new(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                new TaskCompletionDispatcher([Action]),
                new TasksResourceAuthorizer(
                    new FakeRoleAuthorizationService(HrAdministratorRoleId),
                    new FakeDirectReportsReader()),
                JobClient, NullLogger<CompleteTaskHandler>.Instance);

        public async Task SeedTaskAsync()
        {
            await using var db = NewDb();
            var task = TaskItem.Create(
                Guid.NewGuid(), CompanyId, Guid.NewGuid(), "Record feedback", null, TaskPriority.Medium,
                TaskSource.Recruitment, TaskActionType.Complete, null, EmployeeId, null, Now,
                sourceEntityId: Guid.NewGuid());
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
            TaskId = task.Id;
        }

        public async Task<TaskCompletionOperation> SeedOperationAsync(
            Action<TaskCompletionOperation>? mutate = null, Guid? actor = null)
        {
            await using var db = NewDb();
            var operation = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), CompanyId, TaskId, actor ?? Guid.NewGuid(), "approve", "because", Now - TimeSpan.FromMinutes(6));
            mutate?.Invoke(operation);
            db.TaskCompletionOperations.Add(operation);
            await db.SaveChangesAsync();
            return operation;
        }

        public CompleteTaskRequest Request(string? key) =>
            new() { CompanyId = CompanyId, Id = TaskId, CompletedBy = Guid.NewGuid(), IdempotencyKey = key };

        public async Task<Result<CompleteTaskResponse>> CompleteAsync(CompleteTaskRequest request)
        {
            await using var db = NewDb();
            return await Handler(db).HandleAsync(request, CancellationToken.None);
        }

        public async Task<TaskCompletionOperation> OperationAsync()
        {
            await using var db = NewDb();
            return await db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.TaskId == TaskId);
        }

        public async Task<TaskItem> TaskAsync()
        {
            await using var db = NewDb();
            return await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == TaskId);
        }

        public async Task<int> IdempotencyRecordsAsync()
        {
            await using var db = NewDb();
            return await db.IdempotencyRecords.CountAsync();
        }

        public TaskCompletionReconciliationJob Reconciler(TasksDbContext db) =>
            new(db, new TaskCompletionDispatcher([Action]), Clock, JobClient,
                NullLogger<TaskCompletionReconciliationJob>.Instance);

        public void AssertNoSideEffects()
        {
            Assert.Empty(Notifications.Written);
            Assert.Empty(Audit.Published);
            Assert.Empty(JobClient.CreatedJobs);
        }
    }

    [Theory]
    [InlineData("key-1")]
    [InlineData(null)]
    public async Task First_Request_Claims_The_Operation_Before_Dispatching_And_Dispatches_Once(string? key)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        Guid? claimedByDuringDispatch = null;
        DateTimeOffset? leaseDuringDispatch = null;
        rig.Action.Behavior = async _ =>
        {
            var seen = await rig.OperationAsync();
            claimedByDuringDispatch = seen.ClaimedBy;
            leaseDuringDispatch = seen.LeaseExpiresAt;
            return Result.Success();
        };

        var result = await rig.CompleteAsync(rig.Request(key));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, rig.Action.Calls);
        Assert.NotNull(claimedByDuringDispatch);
        Assert.Equal(Now + TaskCompletionOperation.LeaseDuration, leaseDuringDispatch);
        var operation = await rig.OperationAsync();
        Assert.Equal(TaskCompletionOperation.StatusProcessed, operation.Status);
        Assert.Null(operation.ClaimedBy);
        Assert.Equal(operation.Id, Assert.Single(rig.Action.Contexts).DispatchOperationId);
    }

    [Theory]
    [InlineData("key-1")]
    [InlineData("other-key")]
    [InlineData(null)]
    public async Task Request_Finding_A_Live_Claim_By_Another_Worker_Reports_Pending_And_Does_Nothing(string? key)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var operation = await rig.SeedOperationAsync(o => o.Claim(Guid.NewGuid(), Now));

        var result = await rig.CompleteAsync(rig.Request(key));

        Assert.True(result.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsPending, result.Value!.EffectsStatus);
        Assert.Equal(0, rig.Action.Calls);
        rig.AssertNoSideEffects();
        Assert.Equal(0, await rig.IdempotencyRecordsAsync());
        Assert.Equal(TaskItemStatus.Open, (await rig.TaskAsync()).Status);
        var after = await rig.OperationAsync();
        Assert.Equal(operation.Version, after.Version);
        Assert.Equal(operation.ClaimedBy, after.ClaimedBy);
    }

    [Fact]
    public async Task Request_Finding_An_Expired_Claim_Takes_It_Over_And_Dispatches_Once()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var originalActor = Guid.NewGuid();
        await rig.SeedOperationAsync(o => o.Claim(Guid.NewGuid(), Now - TaskCompletionOperation.LeaseDuration - TimeSpan.FromMinutes(1)), originalActor);

        var result = await rig.CompleteAsync(rig.Request("key-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, rig.Action.Calls);
        var context = Assert.Single(rig.Action.Contexts);
        Assert.Equal(originalActor, context.CompletedBy);
        Assert.Equal("approve", context.OutcomeDecision);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.OperationAsync()).Status);
    }

    [Fact]
    public async Task Request_Finding_An_Unclaimed_Pending_Operation_Claims_It_With_A_Version_Guard()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var seeded = await rig.SeedOperationAsync();
        var observedVersion = 0;
        rig.Action.Behavior = async _ =>
        {
            observedVersion = (await rig.OperationAsync()).Version;
            return Result.Success();
        };

        var result = await rig.CompleteAsync(rig.Request(null));

        Assert.True(result.IsSuccess);
        Assert.Equal(seeded.Version + 1, observedVersion);
        Assert.Equal(1, rig.Action.Calls);
    }

    [Fact]
    public async Task Claim_Lost_To_A_Concurrent_Claimer_Is_Treated_As_Another_Worker_Owning_It()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var seeded = await rig.SeedOperationAsync();

        var interceptor = new BeforeFirstSaveInterceptor(async () =>
        {
            await using var winnerDb = rig.NewDb();
            var row = await winnerDb.TaskCompletionOperations.SingleAsync(o => o.Id == seeded.Id);
            var expected = row.Version;
            row.Claim(Guid.NewGuid(), Now);
            Assert.True((await winnerDb.SaveChangesWithConcurrencyAsync(row, expected, "lost", CancellationToken.None)).IsSuccess);
        });

        await using var loserDb = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseInMemoryDatabase(rig.DbName).AddInterceptors(interceptor).Options);

        var result = await rig.Handler(loserDb).HandleAsync(rig.Request("k"), CancellationToken.None);

        Assert.True(interceptor.Fired);

        Assert.True(result.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsPending, result.Value!.EffectsStatus);
        Assert.Equal(0, rig.Action.Calls);
        rig.AssertNoSideEffects();
    }

    [Fact]
    public async Task Dispatch_Exception_Releases_The_Claim_So_A_Retry_Can_Dispatch()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        rig.Action.Behavior = _ => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.CompleteAsync(rig.Request("k")));

        var operation = await rig.OperationAsync();
        Assert.Equal(TaskCompletionOperation.StatusPending, operation.Status);
        Assert.Null(operation.ClaimedBy);

        rig.Action.Behavior = _ => Task.FromResult(Result.Success());
        var retry = await rig.CompleteAsync(rig.Request("k"));

        Assert.True(retry.IsSuccess);
        Assert.Equal(2, rig.Action.Calls);
        Assert.All(rig.Action.Contexts, c => Assert.Equal(operation.Id, c.DispatchOperationId));
    }

    public static TheoryData<string> LoserStates => new()
    {
        TaskCompletionOperation.StatusDispatchApplied,
        TaskCompletionOperation.StatusProcessed,
        TaskCompletionOperation.StatusEffectsTerminalFailure,
        TaskCompletionOperation.StatusDataIntegrityFailure,
        TaskCompletionOperation.StatusEffectsVerified,
        TaskCompletionOperation.StatusWaived,
    };

    [Theory]
    [MemberData(nameof(LoserStates))]
    public async Task Loser_Observing_The_Winners_Operation_Maps_Its_Live_State_Without_Dispatching(string state)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        await rig.SeedOperationAsync(o =>
        {
            switch (state)
            {
                case TaskCompletionOperation.StatusDispatchApplied:
                    o.MarkDispatchApplied(Now);
                    o.Claim(Guid.NewGuid(), Now); break;
                case TaskCompletionOperation.StatusProcessed: o.MarkProcessed(Now); break;
                case TaskCompletionOperation.StatusEffectsTerminalFailure:
                    o.MarkEffectsTerminalFailure(TaskCompletionOperation.CategoryEffectsRetryLimit, "x", Now); break;
                case TaskCompletionOperation.StatusDataIntegrityFailure: o.MarkDataIntegrityFailure("x", Now); break;
                case TaskCompletionOperation.StatusEffectsVerified:
                    o.MarkDataIntegrityFailure("x", Now);
                    o.MarkEffectsVerified(Guid.NewGuid(), Now); break;
                case TaskCompletionOperation.StatusWaived:
                    o.MarkDataIntegrityFailure("x", Now);
                    o.MarkWaived(Guid.NewGuid(), Now); break;
            }
        });

        var result = await rig.CompleteAsync(rig.Request("k"));

        Assert.Equal(0, rig.Action.Calls);
        rig.AssertNoSideEffects();
        switch (state)
        {
            case TaskCompletionOperation.StatusDispatchApplied:
                Assert.Equal(CompletionStatusMapper.EffectsPending, result.Value!.EffectsStatus); break;
            case TaskCompletionOperation.StatusProcessed:
                Assert.Equal(CompletionStatusMapper.EffectsConfirmed, result.Value!.EffectsStatus); break;
            case TaskCompletionOperation.StatusEffectsTerminalFailure:
                Assert.Equal("conflict.effects_terminal_failure", result.Error.Code); break;
            case TaskCompletionOperation.StatusDataIntegrityFailure:
                Assert.Equal("conflict.data_integrity_failure", result.Error.Code); break;
            case TaskCompletionOperation.StatusEffectsVerified:
                Assert.Equal(CompletionStatusMapper.EffectsVerified, result.Value!.EffectsStatus); break;
            case TaskCompletionOperation.StatusWaived:
                Assert.Equal(CompletionStatusMapper.EffectsWaived, result.Value!.EffectsStatus); break;
        }
    }

    [Fact]
    public async Task Live_Claim_Is_Not_Stolen_By_Reconciliation_And_Expired_Claim_Is_Recovered_Exactly_Once()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var originalActor = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var seeded = await rig.SeedOperationAsync(o => o.Claim(owner, Now), originalActor);

        var blocked = await rig.CompleteAsync(rig.Request("k"));
        await using (var db = rig.NewDb())
            await rig.Reconciler(db).ExecuteAsync();

        Assert.Equal(CompletionStatusMapper.EffectsPending, blocked.Value!.EffectsStatus);
        Assert.Equal(0, rig.Action.Calls);
        Assert.Equal(TaskItemStatus.Open, (await rig.TaskAsync()).Status);
        Assert.Equal(owner, (await rig.OperationAsync()).ClaimedBy);

        rig.Clock.UtcNow = FixedNow + TaskCompletionOperation.LeaseDuration + TimeSpan.FromMinutes(1);
        await using (var db = rig.NewDb())
            await rig.Reconciler(db).ExecuteAsync();
        await using (var db = rig.NewDb())
            await rig.Reconciler(db).ExecuteAsync();

        Assert.Equal(1, rig.Action.Calls);
        var context = Assert.Single(rig.Action.Contexts);
        Assert.Equal(originalActor, context.CompletedBy);
        Assert.Equal("approve", context.OutcomeDecision);
        Assert.Equal("because", context.OutcomeReason);
        Assert.Equal(seeded.Id, context.DispatchOperationId);
        Assert.Equal(TaskItemStatus.Completed, (await rig.TaskAsync()).Status);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, (await rig.OperationAsync()).Status);

        var afterRecovery = await rig.CompleteAsync(rig.Request("k"));
        Assert.Equal(CompletionStatusMapper.EffectsPending, afterRecovery.Value!.EffectsStatus);
        Assert.Equal(1, rig.Action.Calls);
    }
}
