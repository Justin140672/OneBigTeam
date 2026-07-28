using HR.Modules.Recruitment;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.MoveApplicationStage;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class MoveApplicationStageHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Valid_Move_Updates_CurrentStageId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var performedBy = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
            },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.CvReview.Id, result.Value!.CurrentStageId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stages.CvReview.Id, saved.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Valid_Move_Adds_Stage_History_Entry()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var performedBy = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
                Notes         = "Passed CV screen.",
            },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var historyEntry = await db.ApplicationStageHistoryEntries.SingleAsync();
        Assert.Equal(application.Id, historyEntry.ApplicationId);
        Assert.Equal(companyId, historyEntry.CompanyId);
        Assert.Equal(stages.ApplicationReceived.Id, historyEntry.PreviousStageId);
        Assert.Equal(stages.CvReview.Id, historyEntry.NewStageId);
        Assert.Equal(performedBy, historyEntry.ChangedByUserId);
        Assert.Equal("Passed CV screen.", historyEntry.Notes);
    }

    [Fact]
    public async Task HandleAsync_Valid_Move_Publishes_Exactly_One_IntegrationEvent_And_One_AuditEvent()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();

        var result = await handler(db, eventPublisher, auditPublisher).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
            },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var integrationEvent = Assert.Single(eventPublisher.PublishedEvents);
        var stageChanged = Assert.IsType<ApplicantStageChangedIntegrationEvent>(integrationEvent);
        Assert.Equal(companyId, stageChanged.CompanyId);
        Assert.Equal(application.Id, stageChanged.ApplicantId);
        Assert.Equal(vacancy.Id, stageChanged.VacancyId);
        Assert.Equal("Application Received", stageChanged.PreviousStage);
        Assert.Equal("CV Review", stageChanged.NewStage);
        Assert.Equal(performedBy, stageChanged.ChangedBy);

        var auditEvent = Assert.Single(auditPublisher.Published);
        var stageChangedAudit = Assert.IsType<ApplicationStageChangedAuditEvent>(auditEvent);
        Assert.Equal(companyId, stageChangedAudit.CompanyId);
        Assert.Equal(application.Id, stageChangedAudit.ApplicationId);
        Assert.Equal(vacancy.Id, stageChangedAudit.VacancyId);
        Assert.Equal(candidate.Id, stageChangedAudit.CandidateId);
        Assert.Equal(stages.ApplicationReceived.Id, stageChangedAudit.PreviousStageId);
        Assert.Equal(stages.CvReview.Id, stageChangedAudit.NewStageId);
        Assert.Equal(performedBy, stageChangedAudit.ChangedBy);
    }

    [Fact]
    public async Task HandleAsync_Move_Off_Terminal_Stage_Returns_Validation_Error_Not_Exception()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Hired.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Move_To_Inactive_Stage_Returns_Validation_Error_And_Does_Not_Publish_Events_Or_Add_History()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        stages.CvReview.SetActiveStatus(false, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, eventPublisher, auditPublisher).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(eventPublisher.PublishedEvents);
        Assert.Empty(auditPublisher.Published);
        Assert.Empty(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Move_Withdrawn_Application_Returns_Validation_Error()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = Guid.NewGuid(),
                VacancyId     = Guid.NewGuid(),
                ApplicationId = Guid.NewGuid(),
                NewStageId    = Guid.NewGuid(),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_New_Stage_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = companyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = Guid.NewGuid(),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId     = otherCompanyId,
                VacancyId     = vacancy.Id,
                ApplicationId = application.Id,
                NewStageId    = stages.CvReview.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static MoveApplicationStageHandler handler(
        RecruitmentDbContext db,
        FakeIntegrationEventPublisher? eventPublisher = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            db,
            new FakeClock(FixedUtcNow),
            new RecruitmentStageChangeRecorder(db, eventPublisher ?? new FakeIntegrationEventPublisher(), auditPublisher ?? new FakeAuditPublisher()));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
