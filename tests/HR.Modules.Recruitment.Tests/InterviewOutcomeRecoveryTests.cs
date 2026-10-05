using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class InterviewOutcomeRecoveryTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid InterviewId);

    private sealed class Rig
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

        public Rig()
        {
            Resolution = new FakeTaskResolution(Completer, Canceller);
        }

        public FakeTaskCompleter Completer { get; } = new();
        public FakeTaskCanceller Canceller { get; } = new();
        public FakeAuditPublisher Audit { get; } = new();
        public FakeTaskCompletionRecovery Recovery { get; } = new();
        public FakeTaskCompletionOperationStateReader StateReader { get; } = new();
        public ListLogger<InterviewOutcomeTaskReconciliationService> Log { get; } = new();
        public FakeTaskResolution Resolution { get; }

        public RecruitmentDbContext NewDb() =>
            new(new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(_dbName).Options);

        public InterviewOutcomeTaskReconciliationService Service(RecruitmentDbContext db) =>
            new(db, Resolution, OutcomeWiring.Delivery(db, Audit), new FakeClock(FixedUtcNow), Log);

        public RetryInterviewOutcomeReconciliationHandler Retry(RecruitmentDbContext db) =>
            new(db, Recovery, Service(db), OutcomeWiring.Repair(db, Audit, StateReader, FixedUtcNow));

        public InterviewOutcomeRepairService Repair(RecruitmentDbContext db) =>
            OutcomeWiring.Repair(db, Audit, StateReader, FixedUtcNow);

        public async Task<List<InterviewOutcomeRepairAction>> ActionsAsync()
        {
            await using var db = NewDb();
            return await db.InterviewOutcomeRepairActions.AsNoTracking().OrderBy(a => a.SequenceNumber).ToListAsync();
        }

        public async Task<Seed> SeedAsync()
        {
            await using var db = NewDb();
            var companyId = Guid.NewGuid();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
            var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"{Guid.NewGuid():N}@example.com", null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
            application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
            var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            db.Interviews.Add(interview);
            await db.SaveChangesAsync();
            return new Seed(companyId, vacancy.Id, application.Id, interview.Id);
        }

        public async Task RecordAsync(Seed seed)
        {
            await using var db = NewDb();
            var result = await OutcomeWiring.Recorder(db, Audit).RecordAsync(
                new RecordInterviewOutcomeRequest
                {
                    CompanyId = seed.CompanyId,
                    VacancyId = seed.VacancyId,
                    ApplicationId = seed.ApplicationId,
                    InterviewId = seed.InterviewId,
                    Outcome = InterviewOutcome.Passed,
                },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        public async Task<InterviewOutcomeTaskReconciliation> LoadAsync()
        {
            await using var db = NewDb();
            return await db.InterviewOutcomeTaskReconciliations.AsNoTracking().SingleAsync();
        }

        public async Task<Seed> BlockedAsync(Guid? taskId = null, Guid? operationId = null)
        {
            var seed = await SeedAsync();
            Resolution.CompletionResult = Terminal(taskId ?? Guid.NewGuid(), operationId ?? Guid.NewGuid());
            await RecordAsync(seed);
            await using var db = NewDb();
            await Service(db).RunAllOutstandingAsync(CancellationToken.None);
            return seed;
        }
    }

    private static TaskResolutionResult Terminal(Guid taskId, Guid operationId) =>
        TaskResolutionResult.Terminal(taskId, operationId, "Completion action failed (validation): bad data", Now);

    private static RetryInterviewOutcomeReconciliationRequest RetryRequest(Seed seed, Guid reconciliationId, string reason = "Fixed") =>
        new() { CompanyId = seed.CompanyId, ReconciliationId = reconciliationId, Reason = reason };

    [Fact]
    public async Task Terminal_Tasks_Result_Blocks_The_Reconciliation_With_Diagnostics_And_Releases_The_Claim()
    {
        var rig = new Rig();
        var taskId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        await rig.BlockedAsync(taskId, operationId);

        var record = await rig.LoadAsync();
        Assert.True(record.IsBlocked);
        Assert.Equal(InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure, record.BlockedCategory);
        Assert.Equal(taskId, record.BlockedTaskId);
        Assert.Equal(operationId, record.BlockedTasksOperationId);
        Assert.Contains("bad data", record.FailureReason);
        Assert.Null(record.ClaimedUntil);
        Assert.Null(record.CompletedAt);

        var error = Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(operationId.ToString(), error.Message);
        Assert.Contains(record.Id.ToString(), error.Message);
    }

    [Fact]
    public async Task Blocked_Reconciliation_Is_Excluded_From_Sweeps_And_Does_Not_Warn_Repeatedly()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var callsAfterBlock = rig.Completer.Calls.Count;
        await using var db = rig.NewDb();

        var sweep1 = await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);
        var sweep2 = await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);
        var perInterview = await rig.Service(db).RunOutstandingForInterviewAsync(
            seed.CompanyId, seed.InterviewId, CancellationToken.None);
        var directRun = await rig.Service(db).RunAsync(
            await db.InterviewOutcomeTaskReconciliations.SingleAsync(), CancellationToken.None);

        Assert.Equal(0, sweep1);
        Assert.Equal(0, sweep2);
        Assert.Equal(0, perInterview);
        Assert.False(directRun);
        Assert.Equal(callsAfterBlock, rig.Completer.Calls.Count);
        Assert.Single(rig.Log.Entries, e => e.Level >= LogLevel.Warning);
        Assert.True((await rig.LoadAsync()).IsBlocked);
    }

    [Fact]
    public async Task Transient_Outstanding_Result_Stays_Retryable_And_Is_Not_Blocked()
    {
        var rig = new Rig();
        var seed = await rig.SeedAsync();
        rig.Resolution.CompletionConfirmed = false;
        await rig.RecordAsync(seed);
        await using var db = rig.NewDb();

        await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);
        await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);

        var record = await rig.LoadAsync();
        Assert.False(record.IsBlocked);
        Assert.Null(record.CompletedAt);
        Assert.Null(record.ClaimedUntil);
        Assert.Equal(2, record.AttemptCount);
    }

    [Fact]
    public async Task Operator_Retry_Resets_Tasks_Unblocks_And_Resumes_To_Completed_Without_Repeating_Audit()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        var blocked = await rig.LoadAsync();
        var operatorUserId = Guid.NewGuid();
        rig.Resolution.CompletionResult = null;
        await using var db = rig.NewDb();

        var result = await rig.Retry(db).HandleAsync(
            RetryRequest(seed, blocked.Id, "Fixed the outcome data"), operatorUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RetryInterviewOutcomeReconciliationHandler.StatusCompleted, result.Value!.Status);
        Assert.True(result.Value.WasBlocked);
        Assert.True(result.Value.TasksCompletionReset);
        var reset = Assert.Single(rig.Recovery.Calls);
        Assert.Equal(operationId, reset.OperationId);
        Assert.Equal(operatorUserId, reset.OperatorUserId);
        Assert.Equal(seed.CompanyId, reset.CompanyId);

        var record = await rig.LoadAsync();
        Assert.False(record.IsBlocked);
        Assert.NotNull(record.CompletedAt);
        Assert.Equal(1, record.RepairCount);
        Assert.Equal(operatorUserId, record.LastRepairedBy);
        Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeRecordedAuditEvent>());
        var repaired = Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>());
        Assert.Equal(operatorUserId, repaired.OperatorUserId);
        Assert.Equal(operationId, repaired.TasksOperationId);
    }

    [Fact]
    public async Task Operator_Retry_Is_Idempotent_For_An_Already_Unblocked_Reconciliation()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Resolution.CompletionResult = null;

        await using var db1 = rig.NewDb();
        await rig.Retry(db1).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);
        await using var db2 = rig.NewDb();
        var second = await rig.Retry(db2).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.False(second.Value!.WasBlocked);
        Assert.Single(rig.Recovery.Calls);
        Assert.Equal(1, (await rig.LoadAsync()).RepairCount);
    }

    [Fact]
    public async Task Operator_Retry_Is_Rejected_When_The_Tasks_Operation_Is_Gone_And_Stays_Blocked()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Recovery.Outcome = TaskCompletionResetOutcome.NotFound;
        await using var db = rig.NewDb();

        var result = await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True((await rig.LoadAsync()).IsBlocked);
    }

    [Fact]
    public async Task Operator_Retry_Of_Another_Company_Reconciliation_Is_NotFound()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        await using var db = rig.NewDb();

        var result = await rig.Retry(db).HandleAsync(
            new RetryInterviewOutcomeReconciliationRequest { CompanyId = Guid.NewGuid(), ReconciliationId = blocked.Id, Reason = "x" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.True((await rig.LoadAsync()).IsBlocked);
        Assert.NotEqual(seed.CompanyId, Guid.Empty);
    }

    [Fact]
    public async Task Missing_Interview_Data_Never_Sets_AuditDeliveredAt_And_Blocks_The_Reconciliation()
    {
        var rig = new Rig();
        var seed = await rig.SeedAsync();
        rig.Audit.SwallowPersistenceFailure = true;
        await rig.RecordAsync(seed);
        await using (var edit = rig.NewDb())
        {
            edit.Interviews.RemoveRange(edit.Interviews);
            await edit.SaveChangesAsync();
        }

        await using var db = rig.NewDb();
        await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);

        var record = await rig.LoadAsync();
        Assert.Null(record.AuditDeliveredAt);
        Assert.True(record.IsBlocked);
        Assert.Equal(InterviewOutcomeTaskReconciliation.BlockedAuditSourceDataMissing, record.BlockedCategory);
        Assert.Contains(seed.InterviewId.ToString(), record.FailureReason);
    }

    [Fact]
    public async Task Delivery_Throws_A_Source_Data_Exception_And_Does_Not_Mark_Delivered_When_Details_Are_Unavailable()
    {
        var rig = new Rig();
        await using var db = rig.NewDb();
        var record = InterviewOutcomeTaskReconciliation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        db.InterviewOutcomeTaskReconciliations.Add(record);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InterviewOutcomeSourceDataMissingException>(
            () => OutcomeWiring.Delivery(db, rig.Audit).DeliverAsync(record, CancellationToken.None));

        Assert.Equal(record.InterviewId, ex.InterviewId);
        Assert.Null(record.AuditDeliveredAt);
        Assert.Equal(0, rig.Audit.PublishCalls);
    }

    private static TaskCompletionOperationState AdjudicatedState(
        Guid operationId, Guid adjudicatedBy, bool waived = false, bool terminal = false) =>
        new(operationId, Guid.NewGuid(), terminal, false, 0, null, null, 1,
            waived ? "waived" : "evidence_retry", waived, adjudicatedBy);

    [Fact]
    public async Task Sweep_Unblocks_After_Evidence_And_Retry_Adjudication_And_The_Reconciliation_Completes_Normally()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        var adjudicator = Guid.NewGuid();
        rig.StateReader.Set(seed.CompanyId, AdjudicatedState(operationId, adjudicator));
        rig.Resolution.CompletionResult = null;

        await using (var db = rig.NewDb())
            Assert.Equal(1, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));
        await using (var db = rig.NewDb())
            await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);

        var record = await rig.LoadAsync();
        Assert.False(record.IsBlocked);
        Assert.NotNull(record.CompletedAt);
        Assert.False(record.IsWaived);
        Assert.Equal(adjudicator, record.LastRepairedBy);
        var action = Assert.Single(await rig.ActionsAsync());
        Assert.Equal(InterviewOutcomeRepairAction.SourceTasksAdjudication, action.Source);
        Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeRecordedAuditEvent>());
    }

    [Fact]
    public async Task Sweep_Keeps_The_Reconciliation_Blocked_While_The_Tasks_Operation_Still_Requires_Investigation()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        rig.StateReader.Set(seed.CompanyId, new TaskCompletionOperationState(
            operationId, Guid.NewGuid(), true, true, 0, null, null));

        await using var db = rig.NewDb();
        Assert.Equal(0, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));

        Assert.True((await rig.LoadAsync()).IsBlocked);
        Assert.Empty(await rig.ActionsAsync());
    }

    [Fact]
    public async Task A_Tasks_Waiver_Closes_The_Reconciliation_As_Waived_And_Is_Never_Reported_As_Plain_Completed()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(taskId, operationId);
        var blocked = await rig.LoadAsync();
        rig.StateReader.Set(seed.CompanyId, AdjudicatedState(operationId, Guid.NewGuid(), waived: true));
        rig.Resolution.CompletionResult = TaskResolutionResult.Waived(taskId, operationId);

        await using (var db = rig.NewDb())
            await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None);
        await using (var db = rig.NewDb())
            await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);
        await using (var db = rig.NewDb())
            await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);

        var record = await rig.LoadAsync();
        Assert.False(record.IsBlocked);
        Assert.NotNull(record.CompletedAt);
        Assert.True(record.IsWaived);
        Assert.Equal(operationId, record.WaivedTasksOperationId);
        Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeRecordedAuditEvent>());

        await using var retryDb = rig.NewDb();
        var response = await rig.Retry(retryDb).HandleAsync(
            RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(RetryInterviewOutcomeReconciliationHandler.StatusCompletedWaived, response.Value!.Status);
        Assert.NotEqual(RetryInterviewOutcomeReconciliationHandler.StatusCompleted, response.Value.Status);
    }

    [Fact]
    public async Task A_Verified_Tasks_Resolution_Completes_The_Reconciliation_Without_A_Waiver_Marker()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        rig.StateReader.Set(seed.CompanyId, new TaskCompletionOperationState(
            operationId, Guid.NewGuid(), false, false, 0, null, null, 1, "effects_verified", false, Guid.NewGuid()));
        rig.Resolution.CompletionResult = TaskResolutionResult.Confirmed(Guid.NewGuid(), operationId);

        await using (var db = rig.NewDb())
            await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None);
        await using (var db = rig.NewDb())
            await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);

        var record = await rig.LoadAsync();
        Assert.NotNull(record.CompletedAt);
        Assert.False(record.IsWaived);
    }

    private sealed class CancellingAudit(CancellationTokenSource cts)
        : HR.SharedKernel.IAuditEventPublisher, HR.Infrastructure.Abstractions.IAuditEventExistenceReader
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private static TaskCompletionOperationState ResetState(Guid operationId, Guid? resetBy = null, bool terminal = false, int resetCount = 1) =>
        new(operationId, Guid.NewGuid(), terminal, false, resetCount, resetBy ?? Guid.NewGuid(), Now);

    [Fact]
    public async Task Operator_Retry_Saves_A_Durable_Intent_With_The_Stable_Event_Id()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Resolution.CompletionResult = null;
        var operatorUserId = Guid.NewGuid();
        await using var db = rig.NewDb();

        var result = await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id, "Fixed"), operatorUserId, CancellationToken.None);

        var action = Assert.Single(await rig.ActionsAsync());
        Assert.Equal(InterviewOutcomeRepairAction.EventIdFor(blocked.Id, 1), action.Id);
        Assert.Equal(action.Id, result.Value!.RecoveryActionId);
        Assert.Equal(seed.CompanyId, action.CompanyId);
        Assert.Equal(seed.InterviewId, action.InterviewId);
        Assert.Equal(blocked.Id, action.ReconciliationId);
        Assert.Equal(operatorUserId, action.OperatorUserId);
        Assert.Equal(1, action.SequenceNumber);
        Assert.Equal(InterviewOutcomeRepairAction.SourceOperator, action.Source);
        Assert.NotNull(action.AuditDeliveredAt);
        var repaired = Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>());
        Assert.Equal(action.Id, ((HR.SharedKernel.IAuditEvent)repaired).EventId);
    }

    [Fact]
    public async Task Unblock_Succeeds_While_Audit_Persistence_Is_Unavailable_And_The_Sweep_Later_Delivers_Without_Repeating_The_Unblock()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Resolution.CompletionResult = null;
        rig.Audit.SwallowPersistenceFailure = true;
        await using (var db = rig.NewDb())
        {
            var result = await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var pending = Assert.Single(await rig.ActionsAsync());
        Assert.Null(pending.AuditDeliveredAt);
        Assert.Equal(1, pending.AuditAttemptCount);
        Assert.NotNull(pending.LastAuditFailure);
        Assert.Empty(rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>());
        Assert.Equal(1, (await rig.LoadAsync()).RepairCount);

        rig.Audit.SwallowPersistenceFailure = false;
        await using (var db = rig.NewDb())
            Assert.Equal(1, await OutcomeWiring.RepairDelivery(db, rig.Audit, FixedUtcNow).DeliverOutstandingAsync(CancellationToken.None));

        Assert.NotNull(Assert.Single(await rig.ActionsAsync()).AuditDeliveredAt);
        var repaired = Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>());
        Assert.Equal(pending.Id, ((HR.SharedKernel.IAuditEvent)repaired).EventId);
        Assert.Equal(1, (await rig.LoadAsync()).RepairCount);
    }

    [Fact]
    public async Task Repeated_Delivery_Of_One_Repair_Creates_Exactly_One_Audit_Event()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Resolution.CompletionResult = null;
        rig.Audit.SwallowPersistenceFailure = true;
        await using (var db = rig.NewDb())
            await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);
        rig.Audit.SwallowPersistenceFailure = false;

        for (var i = 0; i < 3; i++)
        {
            await using var db = rig.NewDb();
            await OutcomeWiring.RepairDelivery(db, rig.Audit, FixedUtcNow).DeliverOutstandingAsync(CancellationToken.None);
        }

        Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>());
    }

    [Fact]
    public async Task Two_Legitimate_Repairs_Create_Two_Distinct_Audit_Events()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Resolution.CompletionResult = null;
        await using (var db = rig.NewDb())
            await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);

        await using (var db = rig.NewDb())
        {
            var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
            record.Block(InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure, "again", Guid.NewGuid(), Guid.NewGuid(), Now);
            await db.SaveChangesAsync();
        }

        await using (var db = rig.NewDb())
            await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);

        var actions = await rig.ActionsAsync();
        Assert.Equal([1, 2], actions.Select(a => a.SequenceNumber));
        Assert.NotEqual(actions[0].Id, actions[1].Id);
        var ids = rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>()
            .Select(e => ((HR.SharedKernel.IAuditEvent)e).EventId).ToList();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task Request_Cancellation_After_The_State_Save_Does_Not_Lose_The_Repair_Intent()
    {
        var rig = new Rig();
        await rig.BlockedAsync();
        using var cts = new CancellationTokenSource();
        var audit = new CancellingAudit(cts);
        var clock = new FakeClock(FixedUtcNow);

        await using (var db = rig.NewDb())
        {
            var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
            var delivery = new InterviewOutcomeRepairAuditDelivery(
                db, audit, audit, clock, NullLogger<InterviewOutcomeRepairAuditDelivery>.Instance);
            var service = new InterviewOutcomeRepairService(
                db, rig.StateReader, delivery, clock, NullLogger<InterviewOutcomeRepairService>.Instance);

            var action = await service.UnblockAsync(
                record, Guid.NewGuid(), "reason", InterviewOutcomeRepairAction.SourceOperator, cts.Token);

            Assert.NotNull(action);
        }

        Assert.False((await rig.LoadAsync()).IsBlocked);
        var pending = Assert.Single(await rig.ActionsAsync());
        Assert.Null(pending.AuditDeliveredAt);
    }

    [Fact]
    public async Task Sweep_Unblocks_A_Reconciliation_Whose_Tasks_Operation_Was_Reset_Directly_And_It_Then_Completes()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        var resetBy = Guid.NewGuid();
        rig.StateReader.Set(seed.CompanyId, ResetState(operationId, resetBy));
        rig.Resolution.CompletionResult = null;

        await using (var db = rig.NewDb())
            Assert.Equal(1, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));
        await using (var db = rig.NewDb())
            await rig.Service(db).RunAllOutstandingAsync(CancellationToken.None);

        var record = await rig.LoadAsync();
        Assert.False(record.IsBlocked);
        Assert.NotNull(record.CompletedAt);
        Assert.Equal(1, record.RepairCount);
        Assert.Equal(resetBy, record.LastRepairedBy);
        var action = Assert.Single(await rig.ActionsAsync());
        Assert.Equal(InterviewOutcomeRepairAction.SourceTasksReset, action.Source);
        Assert.Equal(operationId, action.TasksOperationId);
        Assert.NotNull(action.AuditDeliveredAt);
        Assert.Empty(rig.Recovery.Calls);
    }

    [Fact]
    public async Task Sweep_Leaves_The_Reconciliation_Blocked_When_The_Tasks_Operation_Is_Still_Terminal_Unknown_Or_Never_Reset()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);

        await using (var db = rig.NewDb())
            Assert.Equal(0, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));

        rig.StateReader.Set(seed.CompanyId, ResetState(operationId, terminal: true));
        await using (var db = rig.NewDb())
            Assert.Equal(0, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));

        rig.StateReader.Set(seed.CompanyId, ResetState(operationId, resetCount: 0));
        await using (var db = rig.NewDb())
            Assert.Equal(0, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));

        Assert.True((await rig.LoadAsync()).IsBlocked);
        Assert.Empty(await rig.ActionsAsync());
    }

    [Fact]
    public async Task Sweep_Cannot_Be_Satisfied_By_Another_Companys_Tasks_Operation()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        await rig.BlockedAsync(operationId: operationId);
        rig.StateReader.Set(Guid.NewGuid(), ResetState(operationId));

        await using var db = rig.NewDb();
        Assert.Equal(0, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));

        Assert.True((await rig.LoadAsync()).IsBlocked);
    }

    [Fact]
    public async Task Sweep_Retries_After_A_Tasks_Read_Failure_Without_Throwing()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        rig.StateReader.Set(seed.CompanyId, ResetState(operationId));
        rig.StateReader.ThrowOnRead = true;

        await using (var db = rig.NewDb())
            Assert.Equal(0, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));
        Assert.True((await rig.LoadAsync()).IsBlocked);

        rig.StateReader.ThrowOnRead = false;
        await using (var db = rig.NewDb())
            Assert.Equal(1, await rig.Repair(db).UnblockResetTasksOperationsAsync(CancellationToken.None));
        Assert.False((await rig.LoadAsync()).IsBlocked);
    }

    [Fact]
    public async Task Concurrent_Sweep_And_Operator_Repair_Converge_On_One_Repair_And_One_Audit()
    {
        var rig = new Rig();
        var operationId = Guid.NewGuid();
        var seed = await rig.BlockedAsync(operationId: operationId);
        rig.StateReader.Set(seed.CompanyId, ResetState(operationId));
        rig.Resolution.CompletionResult = null;

        await using var staleDb = rig.NewDb();
        var stale = await staleDb.InterviewOutcomeTaskReconciliations.SingleAsync();

        await using (var sweepDb = rig.NewDb())
            await rig.Repair(sweepDb).UnblockResetTasksOperationsAsync(CancellationToken.None);

        var late = await rig.Repair(staleDb).UnblockAsync(
            stale, Guid.NewGuid(), "late", InterviewOutcomeRepairAction.SourceOperator, CancellationToken.None);

        Assert.Null(late);
        Assert.Equal(1, (await rig.LoadAsync()).RepairCount);
        Assert.Single(await rig.ActionsAsync());
        Assert.Single(rig.Audit.Published.OfType<InterviewOutcomeReconciliationRepairedAuditEvent>());
    }

    [Fact]
    public async Task Operator_Retry_Is_Rejected_For_A_Tasks_Data_Integrity_Failure_And_Stays_Blocked()
    {
        var rig = new Rig();
        var seed = await rig.BlockedAsync();
        var blocked = await rig.LoadAsync();
        rig.Recovery.Outcome = TaskCompletionResetOutcome.DataIntegrityFailure;
        await using var db = rig.NewDb();

        var result = await rig.Retry(db).HandleAsync(RetryRequest(seed, blocked.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True((await rig.LoadAsync()).IsBlocked);
        Assert.Empty(await rig.ActionsAsync());
    }
}
