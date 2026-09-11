using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
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

        var stageChanged = Assert.IsType<HR.SharedKernel.ApplicationStageChangedIntegrationEvent>(Assert.Single(eventPublisher.PublishedEvents));
        Assert.Equal("Interview", stageChanged.PreviousStage);
        Assert.Equal("Offer", stageChanged.NewStage);
        Assert.Equal(performedBy, stageChanged.ChangedBy);

        var stageChangedAudit = Assert.IsType<ApplicationStageChangedAuditEvent>(
            Assert.Single(auditPublisher.Published, e => e is ApplicationStageChangedAuditEvent));
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

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Candidate_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
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

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(stages.Interview.Id, savedApplication.CurrentStageId);
    }

    // SET-05: OfferApprovalRequired gating.

    [Fact]
    public async Task HandleAsync_Fails_When_OfferApprovalRequired_And_Application_Not_Approved()
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

        var settingsReader = new FakeCompanyRecruitmentSettingsReader(
            new CompanyRecruitmentSettings(false, true, 730));

        var result = await handler(db, recruitmentSettingsReader: settingsReader).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("This offer requires approval before it can be made.", result.Error.Message);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stages.Interview.Id, saved.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_OfferApprovalRequired_And_Application_Approved()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.ApproveOffer(Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var settingsReader = new FakeCompanyRecruitmentSettingsReader(
            new CompanyRecruitmentSettings(false, true, 730));

        var result = await handler(db, recruitmentSettingsReader: settingsReader).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.Offer.Id, result.Value!.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_OfferApprovalRequired_Is_False_Regardless_Of_Approval_State()
    {
        // Regression coverage: default settings (OfferApprovalRequired = false) must not change
        // pre-existing OfferCandidate behaviour for an unapproved application.
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

        var result = await handler(db, recruitmentSettingsReader: new FakeCompanyRecruitmentSettingsReader()).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.Offer.Id, result.Value!.CurrentStageId);
    }

    // Ticket 2: offer terms recorded by OfferCandidate.

    private static (Guid companyId, Vacancy vacancy, Application application, RecruitmentStageTestData.SeededStages stages)
        SeedInterviewStageApplication(RecruitmentDbContext db, Guid? positionProfileId = null)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId ?? Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        return (companyId, vacancy, application, stages);
    }

    private static PositionProfileEmploymentDefaults Defaults(Guid id, decimal? salaryMin, string? salaryType) =>
        new(id, "Senior Software Engineer", salaryMin, salaryMin is null ? null : salaryMin + 20000m, salaryType,
            null, null, null, null, null, null);

    [Fact]
    public async Task HandleAsync_Records_Supplied_Offer_Terms_On_Success()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, application, _) = SeedInterviewStageApplication(db);
        await db.SaveChangesAsync();

        var request = new OfferCandidateRequest
        {
            CompanyId              = companyId,
            VacancyId              = vacancy.Id,
            ApplicationId          = application.Id,
            OfferedSalary          = 72000m,
            OfferedSalaryFrequency = "annual",
            ProposedStartDate      = new DateOnly(2026, 9, 1),
            OfferDate              = new DateOnly(2026, 7, 4),
            OfferNotes             = "  Offered top of band.  ",
        };

        var result = await handler(db).HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(72000m, result.Value!.OfferedSalary);
        Assert.Equal("Annual", result.Value.OfferedSalaryFrequency);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value.ProposedStartDate);
        Assert.Equal(new DateOnly(2026, 7, 4), result.Value.OfferDate);
        Assert.Equal("Offered top of band.", result.Value.OfferNotes);
        Assert.Equal("AwaitingResponse", result.Value.OfferResponseStatus);
        Assert.NotNull(result.Value.OfferMadeAt);
        Assert.Null(result.Value.OfferRespondedAt);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(72000m, saved.OfferedSalary);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, saved.OfferResponseStatus);
    }

    [Fact]
    public async Task HandleAsync_PrePopulates_Salary_And_Frequency_From_PositionProfile_When_Request_Omits_Them()
    {
        await using var db = BuildContext();
        var positionProfileId = Guid.NewGuid();
        var (companyId, vacancy, application, _) = SeedInterviewStageApplication(db, positionProfileId);
        await db.SaveChangesAsync();

        var reader = new FakePositionProfileReader(employmentDefaults: new Dictionary<Guid, PositionProfileEmploymentDefaults>
        {
            [positionProfileId] = Defaults(positionProfileId, 60000m, "Annual"),
        });

        var result = await handler(db, reader).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(60000m, result.Value!.OfferedSalary);
        Assert.Equal("Annual", result.Value.OfferedSalaryFrequency);
    }

    [Fact]
    public async Task HandleAsync_Explicit_Request_Salary_Overrides_PositionProfile_Default()
    {
        await using var db = BuildContext();
        var positionProfileId = Guid.NewGuid();
        var (companyId, vacancy, application, _) = SeedInterviewStageApplication(db, positionProfileId);
        await db.SaveChangesAsync();

        var reader = new FakePositionProfileReader(employmentDefaults: new Dictionary<Guid, PositionProfileEmploymentDefaults>
        {
            [positionProfileId] = Defaults(positionProfileId, 60000m, "Annual"),
        });

        var result = await handler(db, reader).HandleAsync(
            new OfferCandidateRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                ApplicationId = application.Id,
                OfferedSalary = 81000m,
                OfferedSalaryFrequency = "Daily",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(81000m, result.Value!.OfferedSalary);
        Assert.Equal("Daily", result.Value.OfferedSalaryFrequency);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Salary_Null_When_Request_And_PositionProfile_Both_Omit_It()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, application, _) = SeedInterviewStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.OfferedSalary);
        Assert.Null(result.Value.OfferedSalaryFrequency);
    }

    [Fact]
    public async Task HandleAsync_Defaults_OfferDate_To_Clock_Today_When_Request_Omits_It()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, application, _) = SeedInterviewStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateOnly.FromDateTime(FixedUtcNow), result.Value!.OfferDate);
    }

    [Fact]
    public async Task HandleAsync_Publishes_OfferDetailsRecordedAuditEvent_Without_Any_Salary_In_Payload()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, application, _) = SeedInterviewStageApplication(db);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new OfferCandidateRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                ApplicationId = application.Id,
                OfferedSalary = 99000m,
                OfferedSalaryFrequency = "Annual",
                ProposedStartDate = new DateOnly(2026, 9, 1),
            },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var offerAudit = Assert.IsType<OfferDetailsRecordedAuditEvent>(
            auditPublisher.Published.Single(e => e is OfferDetailsRecordedAuditEvent));
        Assert.Equal("offer.details_recorded", ((IAuditEvent)offerAudit).EventType);
        Assert.Equal(application.Id, ((IAuditEvent)offerAudit).EntityId);
        Assert.Equal(performedBy, ((IAuditEvent)offerAudit).ActorUserId);

        // Salary must never appear anywhere in the serialised audit payload.
        var json = System.Text.Json.JsonSerializer.Serialize(offerAudit);
        Assert.DoesNotContain("99000", json);
        Assert.DoesNotContain("alary", json);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Record_Offer_Terms_When_SET05_Approval_Gate_Blocks()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, application, stages) = SeedInterviewStageApplication(db);
        await db.SaveChangesAsync();

        var settingsReader = new FakeCompanyRecruitmentSettingsReader(new CompanyRecruitmentSettings(false, true, 730));

        var result = await handler(db, recruitmentSettingsReader: settingsReader).HandleAsync(
            new OfferCandidateRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                ApplicationId = application.Id,
                OfferedSalary = 70000m,
                OfferedSalaryFrequency = "Annual",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stages.Interview.Id, saved.CurrentStageId);
        Assert.Null(saved.OfferedSalary);
        Assert.Null(saved.OfferResponseStatus);
        Assert.Null(saved.OfferMadeAt);
    }

    private static OfferCandidateHandler handler(
        RecruitmentDbContext db,
        FakePositionProfileReader? positionProfileReader = null,
        FakeIntegrationEventPublisher? eventPublisher = null,
        FakeAuditPublisher? auditPublisher = null,
        FakeCompanyRecruitmentSettingsReader? recruitmentSettingsReader = null) =>
        new(db, new FakeClock(FixedUtcNow), positionProfileReader ?? new FakePositionProfileReader(), new RecruitmentStageChangeRecorder(db, eventPublisher ?? new FakeIntegrationEventPublisher(), auditPublisher ?? new FakeAuditPublisher()), recruitmentSettingsReader ?? new FakeCompanyRecruitmentSettingsReader(), auditPublisher ?? new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
