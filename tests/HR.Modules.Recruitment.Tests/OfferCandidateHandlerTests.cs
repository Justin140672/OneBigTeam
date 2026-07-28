using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class OfferCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Moves_NonTerminal_Application_To_The_Last_Active_NonTerminal_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.Offer.Id, result.Value!.CurrentStageId);
        Assert.Equal(vacancy.PositionProfileId, result.Value.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_PositionProfile_EmploymentDefaults_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var leavePolicyId = Guid.NewGuid();
        var employmentDefaults = new Dictionary<Guid, PositionProfileEmploymentDefaults>
        {
            [positionProfileId] = new(
                positionProfileId,
                "Senior Software Engineer",
                SalaryMin: 60000m,
                SalaryMax: 80000m,
                SalaryType: "Annual",
                WorkingDaysOverride: WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
                HoursPerDayOverride: 7.5m,
                ProbationMonthsOverride: 3,
                DefaultLeavePolicyId: leavePolicyId,
                LocationId: Guid.NewGuid(),
                LocationName: "London"),
        };

        var result = await handler(db, new FakePositionProfileReader(employmentDefaults: employmentDefaults)).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value!.PositionProfileId);
        Assert.Equal("Senior Software Engineer", result.Value.PositionProfileTitle);
        Assert.Equal(60000m, result.Value.SalaryMin);
        Assert.Equal(80000m, result.Value.SalaryMax);
        Assert.Equal("Annual", result.Value.SalaryType);
        Assert.Equal(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
            result.Value.WorkingDaysOverride);
        Assert.Equal(7.5m, result.Value.HoursPerDayOverride);
        Assert.Equal(3, result.Value.ProbationMonthsOverride);
        Assert.Equal(leavePolicyId, result.Value.DefaultLeavePolicyId);
        Assert.Equal("London", result.Value.LocationName);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_Null_EmploymentDefaults_When_PositionProfile_Not_Resolvable()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, new FakePositionProfileReader()).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PositionProfileTitle);
        Assert.Null(result.Value.SalaryMin);
        Assert.Null(result.Value.SalaryMax);
        Assert.Null(result.Value.SalaryType);
        Assert.Null(result.Value.WorkingDaysOverride);
        Assert.Null(result.Value.HoursPerDayOverride);
        Assert.Null(result.Value.ProbationMonthsOverride);
        Assert.Null(result.Value.DefaultLeavePolicyId);
        Assert.Null(result.Value.LocationName);
    }

    [Fact]
    public async Task HandleAsync_Publishes_StageChanged_IntegrationEvent_And_AuditEvent_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();

        var result = await handler(db, eventPublisher: eventPublisher, auditPublisher: auditPublisher).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stageChanged = Assert.IsType<HR.SharedKernel.ApplicantStageChangedIntegrationEvent>(Assert.Single(eventPublisher.PublishedEvents));
        Assert.Equal("Interview", stageChanged.PreviousStage);
        Assert.Equal("Offer", stageChanged.NewStage);
        Assert.Equal(performedBy, stageChanged.ChangedBy);

        var stageChangedAudit = Assert.IsType<ApplicationStageChangedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(stages.Interview.Id, stageChangedAudit.PreviousStageId);
        Assert.Equal(stages.Offer.Id, stageChangedAudit.NewStageId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Events_When_Application_Withdrawn()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, eventPublisher: eventPublisher, auditPublisher: auditPublisher).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(eventPublisher.PublishedEvents);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Nair", "priya.nair@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidate.Id, stages.Interview.Id, null, Now);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancyId, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_On_Terminal_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Rejected.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    private static OfferCandidateHandler handler(
        RecruitmentDbContext db,
        FakePositionProfileReader? positionProfileReader = null,
        FakeIntegrationEventPublisher? eventPublisher = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db, new FakeClock(FixedUtcNow), positionProfileReader ?? new FakePositionProfileReader(), new RecruitmentStageChangeRecorder(db, eventPublisher ?? new FakeIntegrationEventPublisher(), auditPublisher ?? new FakeAuditPublisher()));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
