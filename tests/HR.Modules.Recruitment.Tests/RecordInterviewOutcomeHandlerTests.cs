using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class RecordInterviewOutcomeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Records_Outcome_And_Notes_And_Mirrors_Onto_Application_Without_Changing_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var recordedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                InterviewId   = interview.Id,
                Outcome       = InterviewOutcome.Passed,
                Notes         = "Strong technical skills.",
            },
            recordedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InterviewOutcome.Passed, result.Value!.Outcome);
        Assert.Equal("Strong technical skills.", result.Value.Notes);

        // Recording an outcome is metadata-only (ticket #99) — it never moves the application off
        // its current stage; advancing the pipeline remains a separate, explicit action.
        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(stages.Interview.Id, savedApplication.CurrentStageId);
        Assert.Equal(InterviewOutcome.Passed, savedApplication.InterviewOutcome);

        // Exactly one audit event is published now: InterviewOutcomeRecordedAuditEvent. Unlike before
        // ticket #99, recording an outcome no longer transitions the stage, so no
        // ApplicationStageChangedAuditEvent accompanies it.
        var auditEvent = Assert.IsType<InterviewOutcomeRecordedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("interview.outcome_recorded", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal("Interview", ((IAuditEvent)auditEvent).EntityType);
        Assert.Equal(interview.Id, ((IAuditEvent)auditEvent).EntityId);
        Assert.Equal(application.Id, auditEvent.ApplicationId);
        Assert.Equal(vacancy.Id, auditEvent.VacancyId);
        Assert.Equal(candidate.Id, auditEvent.CandidateId);
        Assert.Equal(InterviewOutcome.Passed, auditEvent.Outcome);
        Assert.Equal("Strong technical skills.", auditEvent.Notes);
        Assert.Equal(recordedBy, auditEvent.RecordedBy);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = Guid.NewGuid(),
                VacancyId     = Guid.NewGuid(),
                ApplicationId = Guid.NewGuid(),
                InterviewId   = Guid.NewGuid(),
                Outcome       = InterviewOutcome.Passed,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Interview_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                InterviewId   = Guid.NewGuid(),
                Outcome       = InterviewOutcome.Passed,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Outcome_Already_Recorded()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        interview.RecordOutcome(InterviewOutcome.Failed, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                InterviewId   = interview.Id,
                Outcome       = InterviewOutcome.Passed,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Completes_Feedback_Task_Via_TaskCompleter()
    {
        await using var db = BuildContext();
        var taskCompleter = new FakeTaskCompleter();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var recordedBy = Guid.NewGuid();

        await handler(db, taskCompleter).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                InterviewId   = interview.Id,
                Outcome       = InterviewOutcome.Passed,
            },
            recordedBy,
            CancellationToken.None);

        var call = Assert.Single(taskCompleter.Calls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(interview.Id, call.SourceEntityId);
        Assert.Equal(TaskSource.Recruitment, call.Source);
        Assert.Equal(TaskActionType.Complete, call.ActionType);
        Assert.Equal(recordedBy, call.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Complete_Task_When_Interview_Missing()
    {
        await using var db = BuildContext();
        var taskCompleter = new FakeTaskCompleter();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        await handler(db, taskCompleter).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                InterviewId   = Guid.NewGuid(),
                Outcome       = InterviewOutcome.Passed,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(taskCompleter.Calls);
    }

    private static RecordInterviewOutcomeHandler handler(
        RecruitmentDbContext db,
        FakeTaskCompleter? taskCompleter = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            new InterviewOutcomeRecorder(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher()),
            taskCompleter ?? new FakeTaskCompleter());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
