using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Tests;

public class InterviewOutcomeAuditConfirmationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid InterviewId);

    private static RecruitmentDbContext BuildContext(string name) =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<Seed> SeedAsync(string dbName)
    {
        await using var db = BuildContext(dbName);
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

    private static RecordInterviewOutcomeRequest Request(Seed seed) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.VacancyId,
        ApplicationId = seed.ApplicationId,
        InterviewId = seed.InterviewId,
        Outcome = InterviewOutcome.Passed,
    };

    private static InterviewOutcomeTaskReconciliationService Service(
        RecruitmentDbContext db, FakeAuditPublisher audit, FakeTaskResolution? resolution = null,
        ListLogger<InterviewOutcomeTaskReconciliationService>? logger = null) =>
        new(db,
            resolution ?? new FakeTaskResolution(new FakeTaskCompleter(), new FakeTaskCanceller()),
            OutcomeWiring.Delivery(db, audit),
            new FakeClock(FixedUtcNow),
            logger ?? new ListLogger<InterviewOutcomeTaskReconciliationService>());

    private static async Task<InterviewOutcomeTaskReconciliation> RecordAsync(
        RecruitmentDbContext db, Seed seed, FakeAuditPublisher audit, ListLogger<InterviewOutcomeRecorder>? logger = null)
    {
        var result = await OutcomeWiring.Recorder(db, audit, logger).RecordAsync(Request(seed), Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return await db.InterviewOutcomeTaskReconciliations.SingleAsync(r => r.InterviewId == seed.InterviewId);
    }

    [Fact]
    public async Task Swallowed_Persistence_Failure_Leaves_AuditDeliveredAt_Unset_And_Outcome_Committed()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { SwallowPersistenceFailure = true };
        await using var db = BuildContext(dbName);

        var record = await RecordAsync(db, seed, audit);

        Assert.Null(record.AuditDeliveredAt);
        Assert.Null(record.CompletedAt);
        Assert.Equal(InterviewOutcome.Passed, (await db.Interviews.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task Existence_Reader_False_After_Publish_Leaves_Delivery_Outstanding()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { ExistsOverride = false };
        await using var db = BuildContext(dbName);

        var record = await RecordAsync(db, seed, audit);

        Assert.Equal(1, audit.PublishCalls);
        Assert.Null(record.AuditDeliveredAt);
    }

    [Fact]
    public async Task Existence_Reader_True_After_Publish_Marks_Delivery_Confirmed()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher();
        await using var db = BuildContext(dbName);

        var record = await RecordAsync(db, seed, audit);

        Assert.NotNull(record.AuditDeliveredAt);
        Assert.Single(audit.Published);
        Assert.Equal(seed.InterviewId, ((IAuditEvent)audit.Published[0]).EventId);
    }

    [Fact]
    public async Task Already_Existing_Audit_Event_Is_Confirmed_Without_Republishing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher();
        audit.Seed(new InterviewOutcomeRecordedAuditEvent(
            seed.CompanyId, seed.InterviewId, seed.ApplicationId, seed.VacancyId, Guid.NewGuid(),
            InterviewOutcome.Passed, null, Guid.NewGuid(), Now));
        await using var db = BuildContext(dbName);

        var record = await RecordAsync(db, seed, audit);

        Assert.Equal(0, audit.PublishCalls);
        Assert.NotNull(record.AuditDeliveredAt);
    }

    [Fact]
    public async Task Initial_Failed_Delivery_Logs_A_Warning_With_Workflow_Identifiers()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { SwallowPersistenceFailure = true };
        var logger = new ListLogger<InterviewOutcomeRecorder>();
        await using var db = BuildContext(dbName);

        var record = await RecordAsync(db, seed, audit, logger);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(record.Id.ToString(), warning.Message);
        Assert.Contains(seed.CompanyId.ToString(), warning.Message);
        Assert.Contains(seed.InterviewId.ToString(), warning.Message);
        Assert.Contains(seed.ApplicationId.ToString(), warning.Message);
        Assert.Contains("outstanding", warning.Message);
        Assert.Contains("retried", warning.Message);
    }

    [Fact]
    public async Task Thrown_Initial_Delivery_Failure_Is_Logged_Not_Swallowed_Silently()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { ThrowOnPublish = true };
        var logger = new ListLogger<InterviewOutcomeRecorder>();
        await using var db = BuildContext(dbName);

        var record = await RecordAsync(db, seed, audit, logger);

        Assert.Null(record.AuditDeliveredAt);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("simulated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Later_Successful_Job_Run_Completes_Delivery_And_Repeated_Runs_Publish_Exactly_One_Audit_Event()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { SwallowPersistenceFailure = true };
        await using var db = BuildContext(dbName);
        await RecordAsync(db, seed, audit);

        audit.SwallowPersistenceFailure = false;
        for (var i = 0; i < 3; i++)
            await Service(db, audit).RunAllOutstandingAsync(CancellationToken.None);

        var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.NotNull(record.AuditDeliveredAt);
        Assert.NotNull(record.CompletedAt);
        Assert.Single(audit.Published);
    }

    [Fact]
    public async Task Job_Does_Not_Mark_Delivery_Confirmed_While_The_Publisher_Keeps_Swallowing_And_Logs_Each_Failure()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { SwallowPersistenceFailure = true };
        var logger = new ListLogger<InterviewOutcomeTaskReconciliationService>();
        await using var db = BuildContext(dbName);
        var record = await RecordAsync(db, seed, audit);

        for (var i = 0; i < 2; i++)
        {
            await Service(db, audit, logger: logger).RunAllOutstandingAsync(CancellationToken.None);
        }

        Assert.Null((await db.InterviewOutcomeTaskReconciliations.SingleAsync()).AuditDeliveredAt);
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w =>
        {
            Assert.Contains(record.Id.ToString(), w.Message);
            Assert.Contains(seed.CompanyId.ToString(), w.Message);
            Assert.Contains(seed.InterviewId.ToString(), w.Message);
            Assert.Contains(seed.ApplicationId.ToString(), w.Message);
        });
    }

    [Fact]
    public async Task Reconciliation_Completes_Feedback_Task_With_The_Business_Effect_Already_Applied_Semantics()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher();
        var resolution = new FakeTaskResolution(new FakeTaskCompleter(), new FakeTaskCanceller());
        await using var db = BuildContext(dbName);
        await RecordAsync(db, seed, audit);

        await Service(db, audit, resolution).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Equal(TaskCompletionDispatchMode.BusinessEffectAlreadyApplied, Assert.Single(resolution.CompletionModes));
    }

    [Fact]
    public async Task Reconciliation_Stays_Outstanding_While_Tasks_Owns_An_Unresolved_Operation_Then_Completes_Once_Processed()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher();
        var resolution = new FakeTaskResolution(new FakeTaskCompleter(), new FakeTaskCanceller()) { CompletionConfirmed = false };
        await using var db = BuildContext(dbName);
        await RecordAsync(db, seed, audit);

        for (var i = 0; i < 2; i++)
        {
            await Service(db, audit, resolution).RunAllOutstandingAsync(CancellationToken.None);
            var outstanding = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
            Assert.Null(outstanding.CompletedAt);
            Assert.NotNull(outstanding.FailureReason);
            await db.SaveChangesAsync();
        }

        resolution.CompletionConfirmed = true;
        await Service(db, audit, resolution).RunAllOutstandingAsync(CancellationToken.None);

        var completed = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.NotNull(completed.CompletedAt);
        Assert.Null(completed.FailureReason);
        Assert.Single(audit.Published);
    }
}
