using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class InterviewOutcomeTaskReconciliationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid InterviewId);

    private static RecruitmentDbContext BuildContext(string name) =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(name).Options);
    private static InterviewOutcomeTaskReconciliationService Service(
        RecruitmentDbContext db, FakeTaskCompleter completer, FakeTaskCanceller canceller,
        FakeAuditPublisher? audit = null, FakeTaskResolution? resolution = null) =>
        new(db, resolution ?? new FakeTaskResolution(completer, canceller), OutcomeWiring.Delivery(db, audit ?? new FakeAuditPublisher()),
            new FakeClock(FixedUtcNow), NullLogger<InterviewOutcomeTaskReconciliationService>.Instance);

    private static RecordInterviewOutcomeHandler Handler(
        RecruitmentDbContext db, FakeTaskCompleter completer, FakeTaskCanceller canceller,
        FakeAuditPublisher? audit = null, FakeTaskResolution? resolution = null)
    {
        audit ??= new FakeAuditPublisher();
        return new(OutcomeWiring.Recorder(db, audit),
            Service(db, completer, canceller, audit, resolution));
    }


    private static async Task<Seed> SeedAsync(string dbName, Guid? companyId = null)
    {
        await using var db = BuildContext(dbName);
        companyId ??= Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId.Value, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId.Value, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId.Value, "Emma", "Clarke", $"{Guid.NewGuid():N}@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId.Value, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId.Value, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();
        return new Seed(companyId.Value, vacancy.Id, application.Id, interview.Id);
    }

    private static RecordInterviewOutcomeRequest Request(Seed seed, InterviewOutcome outcome) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.VacancyId,
        ApplicationId = seed.ApplicationId,
        InterviewId = seed.InterviewId,
        Outcome = outcome,
    };

    private static Task<List<InterviewOutcomeTaskReconciliation>> OutstandingAsync(RecruitmentDbContext db) =>
        db.InterviewOutcomeTaskReconciliations.Where(r => r.CompletedAt == null).ToListAsync();

    [Theory]
    [InlineData("Passed")]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    public async Task Recording_Outcome_Persists_Record_And_Immediately_Reconciles_Both_Tasks(string outcomeName)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        var recordedBy = Guid.NewGuid();
        await using var db = BuildContext(dbName);

        var result = await Handler(db, completer, canceller).HandleAsync(
            Request(seed, Enum.Parse<InterviewOutcome>(outcomeName)), recordedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.Equal(seed.CompanyId, record.CompanyId);
        Assert.Equal(seed.InterviewId, record.InterviewId);
        Assert.Equal(recordedBy, record.RecordedBy);
        Assert.NotNull(record.CompletedAt);
        var complete = Assert.Single(completer.Calls);
        Assert.Equal(TaskActionType.Complete, complete.ActionType);
        Assert.Equal(recordedBy, complete.CompletedBy);
        var cancel = Assert.Single(canceller.Calls);
        Assert.Equal(TaskActionType.Review, cancel.ActionType);
        Assert.Equal(new[] { seed.InterviewId }, cancel.SourceEntityIds);
    }

    [Fact]
    public async Task Feedback_Completion_Failure_Leaves_Record_Outstanding_But_Outcome_Is_Recorded()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        await using var db = BuildContext(dbName);

        var result = await Handler(db, new FakeTaskCompleter { Fail = true }, new FakeTaskCanceller())
            .HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InterviewOutcome.Passed, (await db.Interviews.SingleAsync()).Outcome);
        var record = Assert.Single(await OutstandingAsync(db));
        Assert.Equal(1, record.AttemptCount);
        Assert.NotNull(record.FailureReason);
        Assert.Single(await db.InterviewOutcomeTaskReconciliations.ToListAsync());
    }

    [Fact]
    public async Task Review_Cancel_Failure_Leaves_Record_Outstanding_And_Retry_Recovers_After_Feedback_Already_Completed()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller { Fail = true };
        await using var db = BuildContext(dbName);

        Assert.True((await Handler(db, completer, canceller).HandleAsync(
            Request(seed, InterviewOutcome.Failed), Guid.NewGuid(), CancellationToken.None)).IsSuccess);
        Assert.Single(await OutstandingAsync(db));
        Assert.Single(completer.Calls);

        canceller.Fail = false;
        await using var job = BuildContext(dbName);
        Assert.Equal(1, await Service(job, completer, canceller).RunAllOutstandingAsync(CancellationToken.None));

        Assert.Empty(await OutstandingAsync(job));
        Assert.Equal(2, completer.Calls.Count);
        Assert.Single(canceller.Calls);
    }

    [Fact]
    public async Task Review_Cancelled_Then_Completion_Fails_Recovers_On_Retry()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var completer = new FakeTaskCompleter { Fail = true };
        var canceller = new FakeTaskCanceller();
        await using var db = BuildContext(dbName);
        await Handler(db, completer, canceller).HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);
        Assert.Single(await OutstandingAsync(db));

        completer.Fail = false;
        await using var job = BuildContext(dbName);
        await Service(job, completer, canceller).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Empty(await OutstandingAsync(job));
        Assert.Single(completer.Calls);
    }

    [Fact]
    public async Task Job_Processes_Outstanding_Record_And_Reprocessing_Completed_Record_Has_No_Effect()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        await using (var db = BuildContext(dbName))
            await Handler(db, new FakeTaskCompleter { Fail = true }, new FakeTaskCanceller())
                .HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);

        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        await using var jobDb = BuildContext(dbName);
        var job = new InterviewTaskCleanupReconciliationJob(
            new InterviewTaskCleanupService(jobDb, canceller, new FakeClock(FixedUtcNow), NullLogger<InterviewTaskCleanupService>.Instance),
            new InterviewTaskEffectsService(jobDb, new FakeTaskCreator(), canceller, completer, new FakeClock(FixedUtcNow), NullLogger<InterviewTaskEffectsService>.Instance),
            Service(jobDb, completer, canceller),
            OutcomeWiring.Repair(jobDb, new FakeAuditPublisher(), null, FixedUtcNow),
            OutcomeWiring.RepairDelivery(jobDb, new FakeAuditPublisher(), FixedUtcNow));

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Empty(await OutstandingAsync(jobDb));
        Assert.Single(completer.Calls);
        Assert.Single(canceller.Calls);
    }

    [Fact]
    public async Task Two_Workers_Processing_The_Same_Record_Do_Not_Duplicate_Side_Effects()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        await using (var db = BuildContext(dbName))
            await Handler(db, new FakeTaskCompleter { Fail = true }, new FakeTaskCanceller())
                .HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);

        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        await using var dbA = BuildContext(dbName);
        await using var dbB = BuildContext(dbName);
        var recordA = await dbA.InterviewOutcomeTaskReconciliations.SingleAsync();
        var recordB = await dbB.InterviewOutcomeTaskReconciliations.SingleAsync();

        var a = await Service(dbA, completer, canceller).RunAsync(recordA, CancellationToken.None);
        var b = await Service(dbB, completer, canceller).RunAsync(recordB, CancellationToken.None);

        Assert.True(a);
        Assert.False(b);
        Assert.Single(completer.Calls);
        Assert.Single(canceller.Calls);
    }

    [Fact]
    public async Task Record_Is_Scoped_To_Company_And_Interview()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var other = await SeedAsync(dbName);
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        await using var db = BuildContext(dbName);

        await Handler(db, completer, canceller).HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);

        Assert.All(completer.Calls, c =>
        {
            Assert.Equal(seed.CompanyId, c.CompanyId);
            Assert.Equal(seed.InterviewId, c.SourceEntityId);
        });
        Assert.All(canceller.Calls, c =>
        {
            Assert.Equal(seed.CompanyId, c.CompanyId);
            Assert.Equal(new[] { seed.InterviewId }, c.SourceEntityIds);
        });
        Assert.Empty(await db.InterviewOutcomeTaskReconciliations.Where(r => r.InterviewId == other.InterviewId).ToListAsync());
        Assert.Equal(0, await Service(db, completer, canceller)
            .RunOutstandingForInterviewAsync(Guid.NewGuid(), seed.InterviewId, CancellationToken.None));
    }

    [Fact]
    public async Task Rejected_Outcome_Records_No_Reconciliation_And_Failed_Validation_Creates_None()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        await using var db = BuildContext(dbName);
        var handler = Handler(db, new FakeTaskCompleter(), new FakeTaskCanceller());

        Assert.True((await handler.HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None)).IsSuccess);
        Assert.True((await handler.HandleAsync(Request(seed, InterviewOutcome.Failed), Guid.NewGuid(), CancellationToken.None)).IsFailure);

        Assert.Single(await db.InterviewOutcomeTaskReconciliations.ToListAsync());
        Assert.Equal(InterviewOutcome.Passed, (await db.Interviews.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task Audit_Publication_Failure_Keeps_Outcome_Records_Delivery_Outstanding_And_Still_Reconciles_Tasks()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { ThrowOnPublish = true };
        var completer = new FakeTaskCompleter();
        await using var db = BuildContext(dbName);

        var result = await Handler(db, completer, new FakeTaskCanceller(), audit)
            .HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InterviewOutcome.Passed, (await db.Interviews.SingleAsync()).Outcome);
        var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.Null(record.AuditDeliveredAt);
        Assert.Null(record.CompletedAt);
        Assert.Empty(audit.Published);
        Assert.Single(completer.Calls);
    }

    [Fact]
    public async Task Job_Publishes_Outstanding_Audit_Exactly_Once_Across_Repeated_Runs()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher { ThrowOnPublish = true };
        await using (var db = BuildContext(dbName))
            await Handler(db, new FakeTaskCompleter(), new FakeTaskCanceller(), audit)
                .HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);

        audit.ThrowOnPublish = false;
        await using var jobDb = BuildContext(dbName);
        var service = Service(jobDb, new FakeTaskCompleter(), new FakeTaskCanceller(), audit);
        await service.RunAllOutstandingAsync(CancellationToken.None);
        await service.RunAllOutstandingAsync(CancellationToken.None);

        var published = Assert.Single(audit.Published);
        Assert.Equal(seed.InterviewId, published.EventId);
        var record = await jobDb.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.NotNull(record.AuditDeliveredAt);
        Assert.NotNull(record.CompletedAt);
    }

    [Fact]
    public async Task Record_Stays_Outstanding_Until_Task_Effects_Are_Confirmed()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        var resolution = new FakeTaskResolution(completer, canceller) { CompletionConfirmed = false };
        await using var db = BuildContext(dbName);

        await Handler(db, completer, canceller, resolution: resolution)
            .HandleAsync(Request(seed, InterviewOutcome.Passed), Guid.NewGuid(), CancellationToken.None);
        Assert.Single(await OutstandingAsync(db));

        resolution.CompletionConfirmed = true;
        await Service(db, completer, canceller, resolution: resolution).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Empty(await OutstandingAsync(db));
    }

    [Fact]
    public async Task Task_Driven_Recording_Through_Recorder_Reaches_The_Same_Final_State_Via_Job()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var audit = new FakeAuditPublisher();
        await using var db = BuildContext(dbName);
        var recorder = OutcomeWiring.Recorder(db, audit);

        Assert.True((await recorder.RecordAsync(Request(seed, InterviewOutcome.Failed), Guid.NewGuid(), CancellationToken.None)).IsSuccess);
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        await Service(db, completer, canceller, audit).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Single(audit.Published);
        Assert.Single(completer.Calls);
        Assert.Equal(TaskActionType.Review, Assert.Single(canceller.Calls).ActionType);
        Assert.Empty(await OutstandingAsync(db));
    }
}
