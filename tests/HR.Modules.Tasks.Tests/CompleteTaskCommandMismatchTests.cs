using System.Reflection;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class CompleteTaskCommandMismatchTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedNow);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");
    private const string SecretReason = "secret reason text";
    private const string PrivateDescription = "private description text";

    private sealed class Rig
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

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

        public async Task SeedTaskAsync(bool completed = false)
        {
            await using var db = NewDb();
            var task = TaskItem.Create(
                Guid.NewGuid(), CompanyId, Guid.NewGuid(), "Record feedback", PrivateDescription, TaskPriority.Medium,
                TaskSource.Recruitment, TaskActionType.Complete, null, EmployeeId, null, Now,
                sourceEntityId: Guid.NewGuid());
            if (completed)
                task.Complete(Guid.NewGuid(), Now);
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
            TaskId = task.Id;
        }

        public async Task<TaskCompletionOperation> SeedOperationAsync(Action<TaskCompletionOperation>? mutate = null)
        {
            await using var db = NewDb();
            var operation = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), CompanyId, TaskId, Guid.NewGuid(), "Approve", SecretReason, Now - TimeSpan.FromMinutes(6));
            mutate?.Invoke(operation);
            db.TaskCompletionOperations.Add(operation);
            await db.SaveChangesAsync();
            return operation;
        }

        public CompleteTaskRequest Request(string? decision, string? reason, string? key = null) => new()
        {
            CompanyId = CompanyId, Id = TaskId, CompletedBy = Guid.NewGuid(), IdempotencyKey = key,
            OutcomeDecision = decision, OutcomeReason = reason,
        };

        public async Task<Result<CompleteTaskResponse>> CompleteAsync(CompleteTaskRequest request)
        {
            await using var db = NewDb();
            var handler = new CompleteTaskHandler(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                new TaskCompletionDispatcher([Action]),
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

        public async Task<int> IdempotencyRecordsAsync()
        {
            await using var db = NewDb();
            return await db.IdempotencyRecords.CountAsync();
        }

        public void AssertNoMutation()
        {
            Assert.Equal(0, Action.Calls);
            Assert.Empty(Notifications.Written);
            Assert.Empty(Audit.Published);
            Assert.Empty(JobClient.CreatedJobs);
        }
    }

    public static TheoryData<string> SettledStates => new()
    {
        TaskCompletionOperation.StatusPending,
        TaskCompletionOperation.StatusDispatchApplied,
        TaskCompletionOperation.StatusProcessed,
        TaskCompletionOperation.StatusEffectsTerminalFailure,
        TaskCompletionOperation.StatusDataIntegrityFailure,
        TaskCompletionOperation.StatusEffectsVerified,
        TaskCompletionOperation.StatusWaived,
    };

    private static void ApplyState(TaskCompletionOperation o, string state)
    {
        switch (state)
        {
            case TaskCompletionOperation.StatusPending:
                o.Claim(Guid.NewGuid(), Now); break;
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
    }

    private static void AssertMismatch(Result<CompleteTaskResponse> result, TaskCompletionOperation operation)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(CompletionStatusMapper.CommandMismatchCode, result.Error.Code);
        Assert.NotNull(result.Error.Details);
        Assert.Equal(operation.TaskId, result.Error.Details!["taskId"]);
        Assert.Equal(operation.Id, result.Error.Details["operationId"]);
        Assert.Equal(operation.Status, result.Error.Details["status"]);
        Assert.Equal(true, result.Error.Details["refresh"]);
        Assert.Equal(4, result.Error.Details.Count);

        var exposed = result.Error.Message + string.Join('|', result.Error.Details.Values);
        Assert.DoesNotContain("Approve", exposed);
        Assert.DoesNotContain("Reject", exposed);
        Assert.DoesNotContain(SecretReason, exposed);
        Assert.DoesNotContain(PrivateDescription, exposed);
    }

    private static async Task AssertOperationUntouchedAsync(Rig rig, TaskCompletionOperation seeded)
    {
        var after = Assert.Single(await rig.OperationsAsync());
        Assert.Equal(seeded.Id, after.Id);
        Assert.Equal(seeded.Status, after.Status);
        Assert.Equal(seeded.Version, after.Version);
        Assert.Equal(seeded.CompletedBy, after.CompletedBy);
        Assert.Equal(seeded.OutcomeDecision, after.OutcomeDecision);
        Assert.Equal(seeded.OutcomeReason, after.OutcomeReason);
        Assert.Equal(seeded.CommandFingerprint, after.CommandFingerprint);
        Assert.Equal(seeded.CorrelationId, after.CorrelationId);
        Assert.Equal(seeded.MessageId, after.MessageId);
        Assert.Equal(0, await rig.IdempotencyRecordsAsync());
    }

    [Theory]
    [MemberData(nameof(SettledStates))]
    public async Task Conflicting_Decision_Returns_Mismatch_With_No_Mutation_In_Every_State(string state)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var seeded = await rig.SeedOperationAsync(o => ApplyState(o, state));

        foreach (var key in new string?[] { null, "k-1", "k-2" })
        {
            var result = await rig.CompleteAsync(rig.Request("Reject", "different", key));
            AssertMismatch(result, seeded);
        }

        rig.AssertNoMutation();
        await AssertOperationUntouchedAsync(rig, seeded);
    }

    [Theory]
    [MemberData(nameof(SettledStates))]
    public async Task Conflicting_Reason_Returns_Mismatch_With_No_Mutation_In_Every_State(string state)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var seeded = await rig.SeedOperationAsync(o => ApplyState(o, state));

        var result = await rig.CompleteAsync(rig.Request("Approve", "another reason", "k"));

        AssertMismatch(result, seeded);
        rig.AssertNoMutation();
        await AssertOperationUntouchedAsync(rig, seeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_Command_Does_Not_Reclaim_A_Pending_Operation(bool expiredClaim)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var seeded = await rig.SeedOperationAsync(o =>
        {
            if (expiredClaim)
                o.Claim(Guid.NewGuid(), Now - TaskCompletionOperation.LeaseDuration - TimeSpan.FromMinutes(1));
        });

        var result = await rig.CompleteAsync(rig.Request("Reject", "different", "k"));

        AssertMismatch(result, seeded);
        rig.AssertNoMutation();
        await AssertOperationUntouchedAsync(rig, seeded);
        Assert.Equal(TaskItemStatus.Open, (await RigTaskAsync(rig)).Status);
    }

    private static async Task<TaskItem> RigTaskAsync(Rig rig)
    {
        await using var db = rig.NewDb();
        return await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == rig.TaskId);
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusProcessed)]
    [InlineData(TaskCompletionOperation.StatusEffectsTerminalFailure)]
    [InlineData(TaskCompletionOperation.StatusDataIntegrityFailure)]
    [InlineData(TaskCompletionOperation.StatusEffectsVerified)]
    [InlineData(TaskCompletionOperation.StatusWaived)]
    public async Task Conflicting_Command_On_An_Already_Completed_Task_Returns_Mismatch(string state)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync(completed: true);
        var seeded = await rig.SeedOperationAsync(o => ApplyState(o, state));

        var result = await rig.CompleteAsync(rig.Request("Reject", SecretReason, "new-key"));

        AssertMismatch(result, seeded);
        rig.AssertNoMutation();
        await AssertOperationUntouchedAsync(rig, seeded);
    }

    [Theory]
    [MemberData(nameof(SettledStates))]
    public async Task Matching_Command_Follows_The_Existing_Live_State_Mapping_In_Every_State(string state)
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        await rig.SeedOperationAsync(o => ApplyState(o, state));

        var result = await rig.CompleteAsync(rig.Request("  Approve ", $" {SecretReason}  ", "k"));

        rig.AssertNoMutation();
        switch (state)
        {
            case TaskCompletionOperation.StatusPending:
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
    public async Task Rejected_Operation_Does_Not_Block_A_Corrected_Command()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        var rejected = await rig.SeedOperationAsync(o => o.MarkRejected("declined", Now));

        var result = await rig.CompleteAsync(rig.Request("Reject", "corrected", "k"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, rig.Action.Calls);
        var operations = await rig.OperationsAsync();
        Assert.Equal(2, operations.Count);
        var fresh = Assert.Single(operations, o => o.Id != rejected.Id);
        Assert.Equal("Reject", fresh.OutcomeDecision);
        Assert.Equal("corrected", fresh.OutcomeReason);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, fresh.Status);
        Assert.Equal(TaskCompletionOperation.StatusRejected, operations.Single(o => o.Id == rejected.Id).Status);
    }

    [Fact]
    public async Task Dispatch_Rejection_Leaves_The_Task_Open_For_A_Corrected_Retry()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        rig.Action.Behavior = ctx => Task.FromResult(
            ctx.OutcomeDecision == "Approve" ? Result.Success() : Result.Failure(Error.Validation("bad decision")));

        var first = await rig.CompleteAsync(rig.Request("Bogus", null, "k1"));
        var corrected = await rig.CompleteAsync(rig.Request("Approve", null, "k2"));

        Assert.True(first.IsFailure);
        Assert.True(corrected.IsSuccess);
        Assert.Equal(2, rig.Action.Calls);
    }

    [Fact]
    public async Task Legacy_Operation_Without_A_Stored_Fingerprint_Is_Compared_By_Its_Stored_Command()
    {
        var rig = new Rig();
        await rig.SeedTaskAsync();
        TaskCompletionOperation seeded;
        await using (var db = rig.NewDb())
        {
            seeded = await rig.SeedOperationAsync(o => o.Claim(Guid.NewGuid(), Now));
            var row = await db.TaskCompletionOperations.SingleAsync(o => o.Id == seeded.Id);
            db.Entry(row).Property(o => o.CommandFingerprint).CurrentValue = null;
            await db.SaveChangesAsync();
        }

        var matching = await rig.CompleteAsync(rig.Request("Approve", SecretReason));
        var conflicting = await rig.CompleteAsync(rig.Request("Reject", SecretReason));

        Assert.True(matching.IsSuccess);
        AssertMismatch(conflicting, seeded);
        rig.AssertNoMutation();
    }

    [Fact]
    public void Command_Fingerprint_Is_Stable_Normalised_And_Does_Not_Contain_Plaintext()
    {
        var taskId = Guid.NewGuid();
        var baseline = TaskCompletionCommand.Create(taskId, "Approve", SecretReason).Fingerprint();

        Assert.Equal(baseline, TaskCompletionCommand.Create(taskId, "  Approve\t", $"  {SecretReason}\n").Fingerprint());
        Assert.Equal(
            TaskCompletionCommand.Create(taskId, null, null).Fingerprint(),
            TaskCompletionCommand.Create(taskId, "  ", "").Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(taskId, "approve", SecretReason).Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(taskId, "Approve", SecretReason + "!").Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(taskId, "Approve", null).Fingerprint());
        Assert.NotEqual(baseline, TaskCompletionCommand.Create(Guid.NewGuid(), "Approve", SecretReason).Fingerprint());
        Assert.DoesNotContain("Approve", baseline);
        Assert.DoesNotContain(SecretReason, baseline);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), taskId, Guid.NewGuid(), "Approve", SecretReason, Now);
        Assert.Equal(baseline, operation.CommandFingerprint);
    }

    [Fact]
    public void Every_Business_Significant_Request_Property_Is_Part_Of_The_Command()
    {
        var excluded = new HashSet<string> { nameof(CompleteTaskRequest.CompanyId), nameof(CompleteTaskRequest.CompletedBy), nameof(CompleteTaskRequest.IdempotencyKey) };
        var requestProperties = typeof(CompleteTaskRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract" && !excluded.Contains(n))
            .Select(n => n == nameof(CompleteTaskRequest.Id) ? "TaskId" : n.Replace("Outcome", string.Empty))
            .Order()
            .ToArray();
        var commandProperties = typeof(TaskCompletionCommand)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .Order()
            .ToArray();

        Assert.Equal(commandProperties, requestProperties);
    }
}
