using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class InterviewFeedbackServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordFeedbackAsync_Records_Outcome_On_Interview_And_Mirrors_Onto_Application_Without_Changing_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var recordedBy = Guid.NewGuid();

        var result = await service(db).RecordFeedbackAsync(
            companyId, interview.Id, recordedBy, "Passed", "Strong technical skills.", CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.Interviews.SingleAsync();
        Assert.Equal(InterviewOutcome.Passed, saved.Outcome);
        Assert.Equal("Strong technical skills.", saved.Notes);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(InterviewOutcome.Passed, savedApplication.InterviewOutcome);
        Assert.Equal(stages.Interview.Id, savedApplication.CurrentStageId);
    }

    [Fact]
    public async Task RecordFeedbackAsync_Returns_Validation_Error_For_Unrecognised_Outcome()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var result = await service(db).RecordFeedbackAsync(
            companyId, interview.Id, Guid.NewGuid(), "Maybe", null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task RecordFeedbackAsync_Returns_NotFound_When_Interview_Missing()
    {
        await using var db = BuildContext();

        var result = await service(db).RecordFeedbackAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Passed", null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private sealed record Recorded(Guid CompanyId, Guid ApplicationId, Guid InterviewId);

    private static async Task<Recorded> SeedRecordedOutcomeAsync(RecruitmentDbContext db, bool withReconciliation)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"{Guid.NewGuid():N}@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        interview.RecordOutcome(InterviewOutcome.Passed, null, Now);
        application.SetInterviewOutcome(InterviewOutcome.Passed, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        if (withReconciliation)
        {
            db.InterviewOutcomeTaskReconciliations.Add(InterviewOutcomeTaskReconciliation.Create(
                Guid.NewGuid(), companyId, application.Id, interview.Id, Guid.NewGuid(), Now));
        }

        await db.SaveChangesAsync();
        return new Recorded(companyId, application.Id, interview.Id);
    }

    private static Task<Result> ReplayAsync(InterviewFeedbackService svc, Recorded r) =>
        svc.RecordFeedbackAsync(
            r.CompanyId, r.InterviewId, Guid.NewGuid(), "Passed", null, CancellationToken.None, Guid.NewGuid());

    [Fact]
    public async Task Replay_Without_A_Reconciliation_Row_Creates_A_Durable_Repair_Record_And_Confirms_The_Audit()
    {
        await using var db = BuildContext();
        var recorded = await SeedRecordedOutcomeAsync(db, withReconciliation: false);
        var audit = new FakeAuditPublisher();

        var result = await ReplayAsync(service(db, audit), recorded);

        Assert.True(result.IsSuccess);
        var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.Equal(recorded.InterviewId, record.InterviewId);
        Assert.Equal(recorded.ApplicationId, record.ApplicationId);
        Assert.NotNull(record.AuditDeliveredAt);
        Assert.Null(record.CompletedAt);
        Assert.Single(audit.Published);
    }

    [Fact]
    public async Task Replay_With_A_Swallowed_Audit_Failure_Does_Not_Report_Delivery_And_Leaves_A_Durable_Outstanding_Record()
    {
        await using var db = BuildContext();
        var recorded = await SeedRecordedOutcomeAsync(db, withReconciliation: false);
        var audit = new FakeAuditPublisher { SwallowPersistenceFailure = true };

        var result = await ReplayAsync(service(db, audit), recorded);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, audit.PublishCalls);
        var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        Assert.Null(record.AuditDeliveredAt);
        Assert.Null(record.CompletedAt);
        Assert.False(record.IsBlocked);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task Replay_Reuses_The_Existing_Reconciliation_And_Repeated_Replays_Publish_One_Audit()
    {
        await using var db = BuildContext();
        var recorded = await SeedRecordedOutcomeAsync(db, withReconciliation: true);
        var audit = new FakeAuditPublisher();
        var svc = service(db, audit);

        Assert.True((await ReplayAsync(svc, recorded)).IsSuccess);
        Assert.True((await ReplayAsync(svc, recorded)).IsSuccess);

        Assert.Single(await db.InterviewOutcomeTaskReconciliations.ToListAsync());
        Assert.Equal(1, audit.PublishCalls);
        Assert.Single(audit.Published);
        Assert.NotNull((await db.InterviewOutcomeTaskReconciliations.SingleAsync()).AuditDeliveredAt);
    }

    [Fact]
    public async Task Replay_Of_A_Blocked_Reconciliation_Returns_An_Actionable_Failure()
    {
        await using var db = BuildContext();
        var recorded = await SeedRecordedOutcomeAsync(db, withReconciliation: true);
        var record = await db.InterviewOutcomeTaskReconciliations.SingleAsync();
        record.Block(InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure, "bad", null, Guid.NewGuid(), Now);
        await db.SaveChangesAsync();
        var audit = new FakeAuditPublisher();

        var result = await ReplayAsync(service(db, audit), recorded);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("operator attention", result.Error.Message);
        Assert.Equal(0, audit.PublishCalls);
    }

    private static InterviewFeedbackService service(RecruitmentDbContext db, FakeAuditPublisher? audit = null)
    {
        audit ??= new FakeAuditPublisher();
        return new(db, OutcomeWiring.Recorder(db, audit), OutcomeWiring.Delivery(db, audit), new FakeClock(FixedUtcNow));
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
