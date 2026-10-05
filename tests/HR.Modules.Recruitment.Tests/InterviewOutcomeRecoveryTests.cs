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
        public ListLogger<InterviewOutcomeTaskReconciliationService> Log { get; } = new();
        public FakeTaskResolution Resolution { get; }

        public RecruitmentDbContext NewDb() =>
            new(new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(_dbName).Options);

        public InterviewOutcomeTaskReconciliationService Service(RecruitmentDbContext db) =>
            new(db, Resolution, OutcomeWiring.Delivery(db, Audit), new FakeClock(FixedUtcNow), Log);

        public RetryInterviewOutcomeReconciliationHandler Retry(RecruitmentDbContext db) =>
            new(db, Recovery, Service(db), Audit, new FakeClock(FixedUtcNow),
                NullLogger<RetryInterviewOutcomeReconciliationHandler>.Instance);

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
}
