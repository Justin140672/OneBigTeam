using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Features.ListTaskCompletionOperationsRequiringIntervention;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class TaskCompletionAdjudicationTests
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

        public TasksDbContext NewDb() =>
            new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(_dbName).Options);

        public TaskRecoveryAuditDelivery Delivery(TasksDbContext db) =>
            new(db, Audit, Audit, Clock, NullLogger<TaskRecoveryAuditDelivery>.Instance);

        public TaskCompletionAdjudicator Adjudicator(TasksDbContext db) =>
            new(db, Clock, Delivery(db), Notifications, Audit, NullLogger<TaskCompletionAdjudicator>.Instance, null, JobClient);

        public TaskCompletionEffectsJob EffectsJob(TasksDbContext db) =>
            new(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                NullLogger<TaskCompletionEffectsJob>.Instance);

        public CompleteTaskHandler Handler(TasksDbContext db) =>
            new(db, Notifications, Clock, new TaskCompletionAuditDelivery(Audit, Audit),
                new TaskCompletionDispatcher([Action]),
                new TasksResourceAuthorizer(new FakeRoleAuthorizationService(HrAdministratorRoleId), new FakeDirectReportsReader()),
                JobClient, NullLogger<CompleteTaskHandler>.Instance);

        public async Task<(Guid OperationId, Guid TaskId)> SeedAsync(
            string status, bool keepTask = true, bool withSnapshot = true, bool assigned = false, string? category = null,
            DateTimeOffset? createdAt = null, Guid? companyId = null)
        {
            await using var db = NewDb();
            var company = companyId ?? CompanyId;
            var task = TaskItem.Create(
                Guid.NewGuid(), company, Guid.NewGuid(), "Record feedback", "private description", TaskPriority.Medium,
                TaskSource.Recruitment, TaskActionType.Complete, null, assigned ? EmployeeId : null, null, Now,
                sourceEntityId: Guid.NewGuid());
            task.Complete(Guid.NewGuid(), Now);

            var operation = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), company, task.Id, task.CompletedBy!.Value, null, null, createdAt ?? Now);
            if (status != TaskCompletionOperation.StatusPending)
                operation.MarkDispatchApplied(Now);
            if (withSnapshot)
                operation.CaptureCompletionSnapshot(task.AssignedEmployeeId, task.Title, task.Description, "Open", Now, Now);

            switch (status)
            {
                case TaskCompletionOperation.StatusProcessed:
                    operation.MarkProcessed(Now);
                    break;
                case TaskCompletionOperation.StatusEffectsTerminalFailure:
                    operation.MarkEffectsTerminalFailure(category ?? TaskCompletionOperation.CategoryEffectsRetryLimit, "secret exception text", createdAt ?? Now);
                    break;
                case TaskCompletionOperation.StatusDataIntegrityFailure:
                    operation.MarkDataIntegrityFailure("secret exception text", createdAt ?? Now, category ?? TaskCompletionOperation.CategoryEvidenceMissing);
                    break;
                case TaskCompletionOperation.StatusEffectsVerified:
                    operation.MarkDataIntegrityFailure("x", Now);
                    operation.MarkEffectsVerified(Guid.NewGuid(), Now);
                    break;
                case TaskCompletionOperation.StatusWaived:
                    operation.MarkDataIntegrityFailure("x", Now);
                    operation.MarkWaived(Guid.NewGuid(), Now);
                    break;
            }

            if (keepTask)
                db.TaskItems.Add(task);
            db.TaskCompletionOperations.Add(operation);
            await db.SaveChangesAsync();
            return (operation.Id, task.Id);
        }

        public async Task<TaskCompletionOperation> LoadAsync(Guid operationId)
        {
            await using var db = NewDb();
            return await db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operationId);
        }

        public async Task<List<TaskRecoveryAction>> ActionsAsync(Guid operationId)
        {
            await using var db = NewDb();
            return await db.TaskRecoveryActions.AsNoTracking()
                .Where(a => a.OperationId == operationId).OrderBy(a => a.SequenceNumber).ToListAsync();
        }

        public async Task<AdjudicationResult> AdjudicateAsync(
            Guid operationId, string resolution, string reason = "investigated", AdjudicationEvidence? evidence = null,
            Guid? companyId = null, Guid? operatorId = null)
        {
            await using var db = NewDb();
            return await Adjudicator(db).AdjudicateAsync(
                companyId ?? CompanyId, operationId, operatorId ?? Guid.NewGuid(), resolution, reason, evidence, CancellationToken.None);
        }

        public AdjudicationEvidence Evidence(bool notificationRequired = false) =>
            new(notificationRequired, notificationRequired ? EmployeeId : null, Now.AddMinutes(-5), "Open", "Review leave");
    }

    private static CompleteTaskRequest Retry(Guid companyId, Guid taskId) =>
        new() { CompanyId = companyId, Id = taskId, CompletedBy = Guid.NewGuid() };

    [Fact]
    public async Task Retry_Of_A_Processed_Completion_Is_The_Existing_Idempotent_Success()
    {
        var rig = new Rig();
        var (_, taskId) = await rig.SeedAsync(TaskCompletionOperation.StatusProcessed);
        await using var db = rig.NewDb();

        var result = await rig.Handler(db).HandleAsync(Retry(rig.CompanyId, taskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsConfirmed, result.Value!.EffectsStatus);
        Assert.Equal(0, rig.Action.Calls);
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusPending)]
    [InlineData(TaskCompletionOperation.StatusDispatchApplied)]
    public async Task Retry_While_Effects_Are_Pending_Reports_Pending_And_Never_Dispatches(string status)
    {
        var rig = new Rig();
        var (_, taskId) = await rig.SeedAsync(status);
        await using var db = rig.NewDb();

        var result = await rig.Handler(db).HandleAsync(Retry(rig.CompanyId, taskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CompletionStatusMapper.EffectsPending, result.Value!.EffectsStatus);
        Assert.NotEqual(CompletionStatusMapper.EffectsConfirmed, result.Value.EffectsStatus);
        Assert.Equal(0, rig.Action.Calls);
        Assert.Empty(rig.JobClient.CreatedJobs);
    }

    [Fact]
    public async Task Retry_Of_A_Terminal_Effects_Failure_Is_A_Typed_Conflict_With_Safe_Details()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(TaskCompletionOperation.StatusEffectsTerminalFailure);
        await using var db = rig.NewDb();

        var result = await rig.Handler(db).HandleAsync(Retry(rig.CompanyId, taskId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict.effects_terminal_failure", result.Error.Code);
        Assert.Equal(operationId, result.Error.Details!["operationId"]);
        Assert.Equal(taskId, result.Error.Details["taskId"]);
        Assert.Equal(TaskCompletionOperation.StatusEffectsTerminalFailure, result.Error.Details["status"]);
        Assert.Equal(TaskCompletionOperation.CategoryEffectsRetryLimit, result.Error.Details["failureCategory"]);
        Assert.Equal(true, result.Error.Details["resettable"]);
        Assert.Equal("reset", result.Error.Details["recoveryAction"]);
        AssertNoSensitiveText(result.Error);
        Assert.Equal(0, rig.Action.Calls);
    }

    [Fact]
    public async Task Retry_Of_A_Data_Integrity_Failure_Is_A_Typed_Conflict_Requiring_Adjudication()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure);
        await using var db = rig.NewDb();

        var result = await rig.Handler(db).HandleAsync(Retry(rig.CompanyId, taskId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict.data_integrity_failure", result.Error.Code);
        Assert.Equal(operationId, result.Error.Details!["operationId"]);
        Assert.Equal(false, result.Error.Details["resettable"]);
        Assert.Equal("adjudicate", result.Error.Details["recoveryAction"]);
        AssertNoSensitiveText(result.Error);
        Assert.Equal(0, rig.Action.Calls);
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusEffectsVerified, CompletionStatusMapper.EffectsVerified, CompletionStatusMapper.ResolutionVerified)]
    [InlineData(TaskCompletionOperation.StatusWaived, CompletionStatusMapper.EffectsWaived, CompletionStatusMapper.ResolutionWaived)]
    public async Task Retry_Of_An_Operator_Resolved_Completion_Reports_A_Distinct_Resolution_Not_Confirmed(
        string status, string expectedEffects, string expectedResolution)
    {
        var rig = new Rig();
        var (_, taskId) = await rig.SeedAsync(status);
        await using var db = rig.NewDb();

        var result = await rig.Handler(db).HandleAsync(Retry(rig.CompanyId, taskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedEffects, result.Value!.EffectsStatus);
        Assert.Equal(expectedResolution, result.Value.ResolutionType);
        Assert.NotEqual(CompletionStatusMapper.EffectsConfirmed, result.Value.EffectsStatus);
    }

    [Fact]
    public async Task Retry_Of_A_Legacy_Completed_Task_Without_An_Operation_Keeps_Compatibility_And_Replays_Nothing()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(TaskCompletionOperation.StatusProcessed);
        await using (var db = rig.NewDb())
        {
            db.TaskCompletionOperations.RemoveRange(db.TaskCompletionOperations);
            await db.SaveChangesAsync();
        }

        await using var verify = rig.NewDb();
        var result = await rig.Handler(verify).HandleAsync(Retry(rig.CompanyId, taskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.EffectsStatus);
        Assert.Equal(0, rig.Action.Calls);
        Assert.Empty(rig.Audit.Published);
        Assert.Empty(rig.Notifications.Written);
        Assert.NotEqual(Guid.Empty, operationId);
    }

    private static void AssertNoSensitiveText(Error error)
    {
        var text = error.Message + string.Join(" ", error.Details!.Values.Select(v => v?.ToString()));
        Assert.DoesNotContain("secret exception text", text);
        Assert.DoesNotContain("private description", text);
    }

    [Fact]
    public async Task Evidence_And_Retry_Stores_The_Snapshot_Creates_A_Durable_Action_And_Delivers_The_Same_Event_Id()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);
        var operatorId = Guid.NewGuid();

        var result = await rig.AdjudicateAsync(
            operationId, TaskCompletionOperation.ResolutionEvidenceRetry, "Supplied from ticket 42",
            rig.Evidence(notificationRequired: true), operatorId: operatorId);

        Assert.Equal(AdjudicationOutcome.Applied, result.Outcome);
        var operation = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, operation.Status);
        Assert.True(operation.HasCompletionSnapshot);
        Assert.True(operation.NotificationRequired);
        Assert.Equal(rig.EmployeeId, operation.SnapshotAssignedEmployeeId);
        Assert.Null(operation.TerminalFailureAt);
        Assert.Equal(1, operation.AdjudicationCount);
        Assert.Single(rig.JobClient.CreatedJobs);

        var action = Assert.Single(await rig.ActionsAsync(operationId));
        Assert.Equal(TaskRecoveryAction.AdjudicationEventIdFor(operationId, 1), action.Id);
        Assert.Equal(TaskRecoveryAction.ActionAdjudication, action.ActionType);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, action.PreviousStatus);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, action.ResultingStatus);
        Assert.Equal(TaskCompletionOperation.ResolutionEvidenceRetry, action.ResolutionType);
        Assert.True(action.EvidenceSupplied);
        Assert.Equal(operatorId, action.OperatorUserId);
        Assert.Equal(taskId, action.TaskId);
        Assert.NotNull(action.AuditDeliveredAt);

        var evt = Assert.Single(rig.Audit.Published.OfType<TaskCompletionAdjudicatedAuditEvent>());
        Assert.Equal(action.Id, ((IAuditEvent)evt).EventId);
        Assert.DoesNotContain("private description", System.Text.Json.JsonSerializer.Serialize(((IAuditEvent)evt).Metadata));
    }

    [Fact]
    public async Task Evidence_Retry_Creates_Only_The_Proven_Absent_Effects()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", rig.EmployeeId, Now));
        await rig.AdjudicateAsync(
            operationId, TaskCompletionOperation.ResolutionEvidenceRetry, "evidence", rig.Evidence(notificationRequired: true));
        var publishCalls = rig.Audit.PublishCalls;

        await using (var db = rig.NewDb())
            await rig.EffectsJob(db).ProcessAsync(operationId, rig.CompanyId);
        await using (var db = rig.NewDb())
            await rig.EffectsJob(db).ProcessAsync(operationId, rig.CompanyId);

        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.LoadAsync(operationId)).Status);
        Assert.Single(rig.Notifications.Written.Where(n => n.SourceEntityId == taskId));
        Assert.Single(rig.Audit.Published.OfType<TaskCompletedAuditEvent>());
        Assert.Equal(publishCalls, rig.Audit.PublishCalls);
    }

    [Fact]
    public async Task Evidence_Retry_Creates_Both_Effects_When_Both_Are_Absent_And_Never_Dispatches()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);
        await rig.AdjudicateAsync(
            operationId, TaskCompletionOperation.ResolutionEvidenceRetry, "evidence", rig.Evidence(notificationRequired: true));

        await using (var db = rig.NewDb())
            await rig.EffectsJob(db).ProcessAsync(operationId, rig.CompanyId);

        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await rig.LoadAsync(operationId)).Status);
        Assert.Single(rig.Notifications.Written.Where(n => n.SourceEntityId == taskId));
        Assert.Single(rig.Audit.Published.OfType<TaskCompletedAuditEvent>());
        Assert.Equal(0, rig.Action.Calls);
    }

    [Theory]
    [InlineData("Completed", 0)]
    [InlineData("Open", 10)]
    public async Task Evidence_Validation_Rejects_Unsafe_Input_Without_Changing_State(string previousStatus, int minutesInFuture)
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);
        var evidence = new AdjudicationEvidence(false, null, Now.AddMinutes(minutesInFuture), previousStatus, "t");

        var result = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionEvidenceRetry, "x", evidence);

        Assert.Equal(AdjudicationOutcome.InvalidEvidence, result.Outcome);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, (await rig.LoadAsync(operationId)).Status);
        Assert.Empty(await rig.ActionsAsync(operationId));
    }

    [Fact]
    public async Task Evidence_Requiring_A_Notification_Without_An_Employee_Is_Rejected()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);

        var result = await rig.AdjudicateAsync(
            operationId, TaskCompletionOperation.ResolutionEvidenceRetry, "x",
            new AdjudicationEvidence(true, null, Now.AddMinutes(-1), "Open", null));

        Assert.Equal(AdjudicationOutcome.InvalidEvidence, result.Outcome);
    }

    [Fact]
    public async Task Verified_Is_Accepted_Only_After_The_App_Confirms_The_Effects_And_Is_Distinct_From_Processed()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);
        var evidence = rig.Evidence(notificationRequired: true);

        var unverified = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionEffectsVerified, "checked", evidence);
        Assert.Equal(AdjudicationOutcome.VerificationFailed, unverified.Outcome);

        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", rig.EmployeeId, Now));
        var auditOnly = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionEffectsVerified, "checked", evidence);
        Assert.Equal(AdjudicationOutcome.VerificationFailed, auditOnly.Outcome);
        Assert.Empty(await rig.ActionsAsync(operationId));

        await rig.Notifications.WriteAsync(
            Guid.NewGuid(), rig.CompanyId, rig.EmployeeId, "Task completed: x", null, taskId,
            NotificationType.TaskCompleted, NotificationPriority.Normal, Now);
        var verified = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionEffectsVerified, "checked", evidence);

        Assert.Equal(AdjudicationOutcome.Applied, verified.Outcome);
        var operation = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusEffectsVerified, operation.Status);
        Assert.NotEqual(TaskCompletionOperation.StatusProcessed, operation.Status);
        Assert.Null(operation.ProcessedAt);
        Assert.NotNull(operation.ResolvedAt);
        Assert.Equal(TaskCompletionOperation.ResolutionEffectsVerified, operation.ResolutionType);
        var action = Assert.Single(await rig.ActionsAsync(operationId));
        Assert.False(action.EvidenceSupplied);
        Assert.Equal(TaskCompletionOperation.StatusEffectsVerified, action.ResultingStatus);
    }

    [Fact]
    public async Task Verified_Uses_The_Stored_Snapshot_When_Present()
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", null, Now));

        var result = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionEffectsVerified, "checked");

        Assert.Equal(AdjudicationOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task Waiver_Records_A_Distinct_Resolved_State_Never_Processed_And_Audits_It()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, withSnapshot: false);

        var blankReason = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, " ");
        Assert.Equal(AdjudicationOutcome.InvalidEvidence, blankReason.Outcome);

        var result = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, "Unrecoverable legacy data");

        Assert.Equal(AdjudicationOutcome.Applied, result.Outcome);
        var operation = await rig.LoadAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusWaived, operation.Status);
        Assert.Null(operation.ProcessedAt);
        var action = Assert.Single(await rig.ActionsAsync(operationId));
        Assert.Equal(TaskCompletionOperation.ResolutionWaived, action.ResolutionType);
        Assert.NotNull(action.AuditDeliveredAt);
        Assert.Single(rig.Audit.Published.OfType<TaskCompletionAdjudicatedAuditEvent>());
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusProcessed)]
    [InlineData(TaskCompletionOperation.StatusEffectsTerminalFailure)]
    [InlineData(TaskCompletionOperation.StatusDispatchApplied)]
    [InlineData(TaskCompletionOperation.StatusPending)]
    public async Task Every_Outcome_Rejects_Sources_Other_Than_A_Data_Integrity_Failure(string status)
    {
        var rig = new Rig();
        var (operationId, taskId) = await rig.SeedAsync(status);
        rig.Audit.Seed(new TaskCompletedAuditEvent(rig.CompanyId, taskId, Guid.NewGuid(), "Open", null, Now));

        foreach (var resolution in new[]
        {
            TaskCompletionOperation.ResolutionEvidenceRetry,
            TaskCompletionOperation.ResolutionEffectsVerified,
            TaskCompletionOperation.ResolutionWaived,
        })
        {
            var result = await rig.AdjudicateAsync(operationId, resolution, "x", rig.Evidence());
            Assert.Equal(AdjudicationOutcome.InvalidState, result.Outcome);
        }

        Assert.Equal(status, (await rig.LoadAsync(operationId)).Status);
        Assert.Empty(await rig.ActionsAsync(operationId));
    }

    [Fact]
    public async Task Adjudicating_Another_Companys_Operation_Is_NotFound()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);

        var result = await rig.AdjudicateAsync(
            operationId, TaskCompletionOperation.ResolutionWaived, "x", companyId: Guid.NewGuid());

        Assert.Equal(AdjudicationOutcome.NotFound, result.Outcome);
        Assert.Empty(await rig.ActionsAsync(operationId));
    }

    [Fact]
    public async Task Standard_Reset_Still_Rejects_A_Data_Integrity_Failure()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);
        await using var db = rig.NewDb();
        var recovery = new TaskCompletionRecovery(db, Clock, rig.Delivery(db), NullLogger<TaskCompletionRecovery>.Instance);

        var reset = await recovery.ResetTerminalCompletionAsync(
            rig.CompanyId, operationId, Guid.NewGuid(), "reset", CancellationToken.None);

        Assert.Equal(TaskCompletionResetOutcome.DataIntegrityFailure, reset.Outcome);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, (await rig.LoadAsync(operationId)).Status);
    }

    [Fact]
    public async Task Duplicate_Adjudication_Is_Idempotent_And_A_Different_Outcome_After_Resolution_Is_Rejected()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);

        var first = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, "x");
        var second = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, "x");
        var other = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionEffectsVerified, "x");

        Assert.Equal(AdjudicationOutcome.Applied, first.Outcome);
        Assert.Equal(AdjudicationOutcome.AlreadyApplied, second.Outcome);
        Assert.Equal(AdjudicationOutcome.InvalidState, other.Outcome);
        Assert.Single(await rig.ActionsAsync(operationId));
        Assert.Single(rig.Audit.Published.OfType<TaskCompletionAdjudicatedAuditEvent>());
        Assert.Equal(1, (await rig.LoadAsync(operationId)).AdjudicationCount);
    }

    [Fact]
    public async Task Concurrent_Adjudications_Have_One_Winner_And_A_Clear_Outcome_For_The_Loser()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);

        await using var staleDb = rig.NewDb();
        await staleDb.TaskCompletionOperations.SingleAsync(o => o.Id == operationId);

        var winner = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, "winner");

        var loser = await rig.Adjudicator(staleDb).AdjudicateAsync(
            rig.CompanyId, operationId, Guid.NewGuid(), TaskCompletionOperation.ResolutionEvidenceRetry, "loser",
            rig.Evidence(), CancellationToken.None);
        var sameOutcomeLoser = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, "same");

        Assert.Equal(AdjudicationOutcome.Applied, winner.Outcome);
        Assert.Equal(AdjudicationOutcome.Concurrency, loser.Outcome);
        Assert.Equal(AdjudicationOutcome.AlreadyApplied, sameOutcomeLoser.Outcome);
        Assert.Single(await rig.ActionsAsync(operationId));
        Assert.Equal(TaskCompletionOperation.StatusWaived, (await rig.LoadAsync(operationId)).Status);
    }

    [Fact]
    public async Task Audit_Delivery_Failure_Is_Retried_With_The_Same_Event_Id_Without_Repeating_The_Transition()
    {
        var rig = new Rig();
        var (operationId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);
        rig.Audit.SwallowPersistenceFailure = true;

        var result = await rig.AdjudicateAsync(operationId, TaskCompletionOperation.ResolutionWaived, "x");

        Assert.Equal(AdjudicationOutcome.Applied, result.Outcome);
        var pending = Assert.Single(await rig.ActionsAsync(operationId));
        Assert.Null(pending.AuditDeliveredAt);
        Assert.NotNull(pending.LastAuditFailure);
        Assert.Empty(rig.Audit.Published.OfType<TaskCompletionAdjudicatedAuditEvent>());

        rig.Audit.SwallowPersistenceFailure = false;
        await using (var db = rig.NewDb())
            await rig.Delivery(db).DeliverOutstandingAsync(CancellationToken.None);
        await using (var db = rig.NewDb())
            await rig.Delivery(db).DeliverOutstandingAsync(CancellationToken.None);

        Assert.NotNull(Assert.Single(await rig.ActionsAsync(operationId)).AuditDeliveredAt);
        var evt = Assert.Single(rig.Audit.Published.OfType<TaskCompletionAdjudicatedAuditEvent>());
        Assert.Equal(pending.Id, ((IAuditEvent)evt).EventId);
        Assert.Equal(1, (await rig.LoadAsync(operationId)).AdjudicationCount);
    }

    [Fact]
    public async Task State_Reader_Exposes_Adjudication_And_Waiver_As_Non_Terminal()
    {
        var rig = new Rig();
        var (waivedId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);
        var (integrityId, _) = await rig.SeedAsync(TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false);
        var operatorId = Guid.NewGuid();
        await rig.AdjudicateAsync(waivedId, TaskCompletionOperation.ResolutionWaived, "x", operatorId: operatorId);

        await using var db = rig.NewDb();
        var reader = new TaskCompletionOperationStateReader(db);

        var waived = await reader.GetAsync(rig.CompanyId, waivedId, CancellationToken.None);
        Assert.False(waived!.IsTerminal);
        Assert.False(waived.IsDataIntegrityFailure);
        Assert.True(waived.IsWaived);
        Assert.Equal(1, waived.AdjudicationCount);
        Assert.Equal(operatorId, waived.LastAdjudicatedBy);
        var integrity = await reader.GetAsync(rig.CompanyId, integrityId, CancellationToken.None);
        Assert.True(integrity!.IsTerminal);
        Assert.True(integrity.IsDataIntegrityFailure);
    }

    [Fact]
    public async Task Typed_Resolution_Reports_Verified_As_Confirmed_And_Waived_As_Waived_Never_Confirmed()
    {
        static ProgrammaticCompletionHarness NewHarness()
        {
            var dbName = Guid.NewGuid().ToString("N");
            return new ProgrammaticCompletionHarness(() =>
                new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(dbName).Options));
        }

        var verifiedHarness = NewHarness();
        var verifiedTask = await verifiedHarness.AddTaskAsync(TaskItemStatus.Completed);
        await verifiedHarness.AddOperationAsync(verifiedTask.Id, TaskCompletionOperation.StatusEffectsVerified);
        var verified = await verifiedHarness.ResolveAsync();

        var waivedHarness = NewHarness();
        var waivedTask = await waivedHarness.AddTaskAsync(TaskItemStatus.Completed);
        var waivedOperation = await waivedHarness.AddOperationAsync(waivedTask.Id, TaskCompletionOperation.StatusWaived);
        var waived = await waivedHarness.ResolveAsync();

        Assert.Equal(TaskResolutionStatus.Confirmed, verified.Status);
        Assert.Equal(TaskResolutionStatus.Waived, waived.Status);
        Assert.False(waived.IsConfirmed);
        Assert.Equal(waivedOperation, waived.OperationId);
    }

    [Fact]
    public async Task Management_Query_Filters_Orders_Paginates_And_Is_Company_Scoped()
    {
        var rig = new Rig();
        var older = await rig.SeedAsync(
            TaskCompletionOperation.StatusEffectsTerminalFailure, createdAt: Now.AddHours(-5));
        var newer = await rig.SeedAsync(
            TaskCompletionOperation.StatusDataIntegrityFailure, keepTask: false, createdAt: Now.AddHours(-1),
            category: TaskCompletionOperation.CategoryNotificationUnconfirmed);
        await rig.SeedAsync(TaskCompletionOperation.StatusProcessed);
        await rig.SeedAsync(TaskCompletionOperation.StatusEffectsTerminalFailure, companyId: Guid.NewGuid());

        await using var db = rig.NewDb();
        var handler = new ListTaskCompletionOperationsRequiringInterventionHandler(db);
        ListTaskCompletionOperationsRequiringInterventionRequest Req(Action<ListTaskCompletionOperationsRequiringInterventionRequest>? _ = null) =>
            new() { CompanyId = rig.CompanyId };

        var all = (await handler.HandleAsync(Req(), CancellationToken.None)).Value!;
        Assert.Equal(2, all.TotalCount);
        Assert.Equal([older.OperationId, newer.OperationId], all.Items.Select(i => i.OperationId));
        Assert.True(all.Items[0].IsResettable);
        Assert.False(all.Items[0].RequiresInvestigation);
        Assert.Equal("awaiting_reset", all.Items[0].RecoveryStatus);
        Assert.True(all.Items[1].RequiresInvestigation);
        Assert.Equal("awaiting_adjudication", all.Items[1].RecoveryStatus);

        var byStatus = (await handler.HandleAsync(
            new() { CompanyId = rig.CompanyId, Status = TaskCompletionOperation.StatusDataIntegrityFailure }, CancellationToken.None)).Value!;
        Assert.Equal([newer.OperationId], byStatus.Items.Select(i => i.OperationId));

        var byCategory = (await handler.HandleAsync(
            new() { CompanyId = rig.CompanyId, FailureCategory = TaskCompletionOperation.CategoryNotificationUnconfirmed }, CancellationToken.None)).Value!;
        Assert.Equal([newer.OperationId], byCategory.Items.Select(i => i.OperationId));

        var byTask = (await handler.HandleAsync(
            new() { CompanyId = rig.CompanyId, TaskId = older.TaskId }, CancellationToken.None)).Value!;
        Assert.Equal([older.OperationId], byTask.Items.Select(i => i.OperationId));

        var byOperation = (await handler.HandleAsync(
            new() { CompanyId = rig.CompanyId, OperationId = newer.OperationId }, CancellationToken.None)).Value!;
        Assert.Single(byOperation.Items);

        var page2 = (await handler.HandleAsync(
            new() { CompanyId = rig.CompanyId, PageNumber = 2, PageSize = 1 }, CancellationToken.None)).Value!;
        Assert.Equal([newer.OperationId], page2.Items.Select(i => i.OperationId));
        Assert.Equal(2, page2.TotalCount);

        var otherCompany = (await handler.HandleAsync(
            new() { CompanyId = Guid.NewGuid(), OperationId = older.OperationId }, CancellationToken.None)).Value!;
        Assert.Empty(otherCompany.Items);
    }
}
