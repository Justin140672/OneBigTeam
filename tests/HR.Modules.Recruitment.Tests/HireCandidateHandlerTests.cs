using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.HireCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class HireCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static HireCandidateRequest BuildRequest(Guid companyId, Guid vacancyId, Guid applicationId) => new()
    {
        CompanyId     = companyId,
        VacancyId     = vacancyId,
        ApplicationId = applicationId,
        StartDate     = new DateOnly(2026, 8, 1),
        DateOfBirth   = new DateOnly(1995, 3, 20),
        Nationality   = "British",
        Gender        = "Female",
    };

    private static (RecruitmentDbContext db, Guid companyId, Vacancy vacancy, Guid departmentId, Guid locationId, FakePositionProfileReader reader, RecruitmentStageTestData.SeededStages stages)
        SeedVacancyWithResolvableProfile(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Software Engineer", null, Guid.NewGuid(), Now, employmentTypeId: Guid.NewGuid());
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Senior Software Engineer", departmentId, true, locationId, "London"),
        };

        return (db, companyId, vacancy, departmentId, locationId, new FakePositionProfileReader(summaries: summaries), stages);
    }

    [Fact]
    public async Task HandleAsync_Hires_NonTerminal_Application_And_Provisions_Employee()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, departmentId, locationId, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", "+44 7700 900001", Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, provisioning, positionProfileReader: reader, auditPublisher: auditPublisher).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.Hired.Id, result.Value!.CurrentStageId);
        Assert.NotEqual(Guid.Empty, result.Value.EmployeeId);

        var savedCandidate = await db.Candidates.SingleAsync();
        Assert.Equal(result.Value.EmployeeId, savedCandidate.EmployeeId);

        var request = Assert.Single(provisioning.Requests);
        Assert.Equal("Emma", request.FirstName);
        Assert.Equal("Clarke", request.LastName);
        Assert.Equal("emma.clarke@example.com", request.WorkEmail);
        Assert.Equal("+44 7700 900001", request.PhoneNumber);
        Assert.Equal(departmentId, request.DepartmentId);
        Assert.Equal(locationId, request.LocationId);
        Assert.Equal(vacancy.PositionProfileId, request.PositionProfileId);

        Assert.Equal(2, auditPublisher.Published.Count);
        var auditEvent = Assert.IsType<CandidateHiredAuditEvent>(auditPublisher.Published.Single(e => e is CandidateHiredAuditEvent));
        Assert.Equal("candidate.hired", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal("Candidate", ((IAuditEvent)auditEvent).EntityType);
        Assert.Equal(candidate.Id, ((IAuditEvent)auditEvent).EntityId);
        Assert.Equal(result.Value.EmployeeId, ((IAuditEvent)auditEvent).EmployeeId);
        Assert.Equal(application.Id, auditEvent.ApplicationId);
        Assert.Equal(vacancy.Id, auditEvent.VacancyId);
        Assert.Equal(result.Value.EmployeeId, auditEvent.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_CandidateHiredIntegrationEvent()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var eventPublisher = new FakeIntegrationEventPublisher();
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", "+44 7700 900001", Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, eventPublisher, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(2, eventPublisher.PublishedEvents.Count);
        var candidateHired = Assert.IsType<CandidateHiredIntegrationEvent>(
            eventPublisher.PublishedEvents.Single(e => e is CandidateHiredIntegrationEvent));
        Assert.Equal(companyId, candidateHired.CompanyId);
        Assert.Equal(application.Id, candidateHired.ApplicationId);
        Assert.Equal(candidate.Id, candidateHired.CandidateId);
        Assert.Equal(result.Value!.EmployeeId, candidateHired.EmployeeId);
        Assert.Equal(vacancy.Id, candidateHired.VacancyId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Event_When_Application_Withdrawn()
    {
        await using var db = BuildContext();
        var eventPublisher = new FakeIntegrationEventPublisher();
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, eventPublisher: eventPublisher, auditPublisher: auditPublisher, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(eventPublisher.PublishedEvents);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_On_Terminal_Stage()
    {
        await using var db = BuildContext();
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Rejected.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_No_Active_Hired_Stage_Configured()
    {
        await using var db = BuildContext();
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        stages.Hired.SetActiveStatus(false, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Linked_To_Employee()
    {
        await using var db = BuildContext();
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, Now);
        candidate.LinkToEmployee(Guid.NewGuid(), Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Hire_Application_When_Provisioning_Fails()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService(Result.Failure<Guid>(Error.Conflict("Work email already exists.")));
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Noah", "Patel", "noah.patel@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(stages.Offer.Id, savedApplication.CurrentStageId);

        var savedCandidate = await db.Candidates.SingleAsync();
        Assert.Null(savedCandidate.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Ivy", "Wren", "ivy.wren@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidate.Id, stages.Offer.Id, null, Now);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            BuildRequest(companyId, vacancyId, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Summary_Not_Resolvable()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, employmentTypeId: Guid.NewGuid());
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Zara", "Osei", "zara.osei@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, positionProfileReader: new FakePositionProfileReader()).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PositionProfile_Has_No_Department()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now, employmentTypeId: Guid.NewGuid());
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Milo", "Adeyemi", "milo.adeyemi@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Backend Engineer", null, true, Guid.NewGuid(), "London"),
        };

        var result = await handler(db, positionProfileReader: new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PositionProfile_Has_No_Location()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now, employmentTypeId: Guid.NewGuid());
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Nadia", "Farouk", "nadia.farouk@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Backend Engineer", Guid.NewGuid(), true, null, null),
        };

        var result = await handler(db, positionProfileReader: new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    // Ticket 2: offer-response gating, offer start-date fallback, and offer compensation hand-off.

    private static (RecruitmentDbContext db, Guid companyId, Vacancy vacancy, FakePositionProfileReader reader, Candidate candidate, Application application, RecruitmentStageTestData.SeededStages stages)
        SeedOfferStageApplication(RecruitmentDbContext db)
    {
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", "+44 7700 900001", Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        return (db, companyId, vacancy, reader, candidate, application, stages);
    }

    [Theory]
    [InlineData((int)OfferResponseStatus.Declined)]
    [InlineData((int)OfferResponseStatus.Withdrawn)]
    public async Task HandleAsync_Blocks_Hire_When_Offer_Was_Declined_Or_Withdrawn(int responseRaw)
    {
        var response = (OfferResponseStatus)responseRaw;
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, stages) = SeedOfferStageApplication(db);
        application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 9, 1), new DateOnly(2026, 7, 4), null, Now);
        application.RespondToOffer(response, Now);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(provisioning.Requests);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stages.Offer.Id, saved.CurrentStageId);
        var savedCandidate = await db.Candidates.SingleAsync();
        Assert.Null(savedCandidate.EmployeeId);
    }

    [Theory]
    [InlineData((int)OfferResponseStatus.Accepted)]
    [InlineData((int)OfferResponseStatus.AwaitingResponse)]
    public async Task HandleAsync_Allows_Hire_When_Offer_Accepted_Or_Still_Awaiting_Response(int responseRaw)
    {
        var response = (OfferResponseStatus)responseRaw;
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 9, 1), new DateOnly(2026, 7, 4), null, Now);
        if (response != OfferResponseStatus.AwaitingResponse)
            application.RespondToOffer(response, Now);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(provisioning.Requests);
    }

    [Fact]
    public async Task HandleAsync_Allows_Hire_When_No_Offer_Was_Ever_Recorded_Legacy_Path()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Offer_ProposedStartDate_When_Request_StartDate_Is_Null()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 9, 15), new DateOnly(2026, 7, 4), null, Now);
        application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { StartDate = null },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 15), Assert.Single(provisioning.Requests).StartDate);
    }

    [Fact]
    public async Task HandleAsync_Request_StartDate_Overrides_Offer_ProposedStartDate()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 9, 15), new DateOnly(2026, 7, 4), null, Now);
        application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { StartDate = new DateOnly(2026, 8, 1) },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 8, 1), Assert.Single(provisioning.Requests).StartDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_No_StartDate_Anywhere()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, stages) = SeedOfferStageApplication(db);
        application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, offeredStartDate: null, new DateOnly(2026, 7, 4), null, Now);
        application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { StartDate = null },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(provisioning.Requests);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stages.Offer.Id, saved.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Passes_Offer_Salary_And_Frequency_Into_Provisioning_Request()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        application.RecordOfferTerms(64250m, OfferSalaryFrequency.Daily, new DateOnly(2026, 9, 1), new DateOnly(2026, 7, 4), null, Now);
        application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var provisioned = Assert.Single(provisioning.Requests);
        Assert.Equal(64250m, provisioned.Salary);
        Assert.Equal("Daily", provisioned.SalaryFrequency);
    }

    [Fact]
    public async Task HandleAsync_Passes_Null_Offer_Compensation_Into_Provisioning_When_No_Offer_Salary_Recorded()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var provisioned = Assert.Single(provisioning.Requests);
        Assert.Null(provisioned.Salary);
        Assert.Null(provisioned.SalaryFrequency);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_For_Internal_Application_And_Never_Provisions_An_Employee()
    {
        // Internal recruitment Ticket 7: an internal applicant is already an employee; hiring would
        // create a second Employee record, so the internal appointment workflow must be used instead.
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();
        var (_, companyId, vacancy, _, _, reader, stages) = SeedVacancyWithResolvableProfile(db);
        db.Vacancies.Add(vacancy);
        var (candidate, application) = InternalApplicationTestData.AddInternal(
            db, companyId, vacancy.Id, stages.Offer.Id, Guid.NewGuid(), Now);
        await db.SaveChangesAsync();
        var linkedEmployeeId = candidate.EmployeeId;

        var result = await handler(db, provisioning, eventPublisher, reader, auditPublisher).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(provisioning.Requests);
        Assert.Empty(eventPublisher.PublishedEvents);
        Assert.Empty(auditPublisher.Published);

        db.ChangeTracker.Clear();
        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stages.Offer.Id, saved.CurrentStageId);
        Assert.Null(saved.AppointmentStatus);
        Assert.Equal(linkedEmployeeId, (await db.Candidates.SingleAsync()).EmployeeId);
        Assert.Empty(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Uses_The_Vacancy_EmploymentType_When_Provisioning()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var provisioned = Assert.Single(provisioning.Requests);
        Assert.Equal(vacancy.EmploymentTypeId, provisioned.EmploymentTypeId);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Hire_When_Legacy_Vacancy_Has_No_EmploymentType()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var legacyVacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Legacy", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, legacyVacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        db.Vacancies.Add(legacyVacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Legacy", Guid.NewGuid(), true, Guid.NewGuid(), "London"),
        };

        var result = await handler(db, provisioning, positionProfileReader: new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            BuildRequest(companyId, legacyVacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("Employment type required", result.Error.Message);
        Assert.Empty(provisioning.Requests);
        Assert.Null((await db.Candidates.SingleAsync()).EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Defaults_Manager_To_Vacancy_HiringManager_When_No_Override()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vacancy.HiringManagerId, Assert.Single(provisioning.Requests).ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Ignores_ManagerId_When_Override_Flag_Is_Not_Set()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { ManagerId = Guid.NewGuid() },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vacancy.HiringManagerId, Assert.Single(provisioning.Requests).ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Honours_Explicit_Alternative_Manager()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();
        var alternativeManagerId = Guid.NewGuid();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { OverrideManager = true, ManagerId = alternativeManagerId },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(alternativeManagerId, Assert.Single(provisioning.Requests).ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Honours_Explicit_No_Manager()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { OverrideManager = true, ManagerId = null },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(provisioning.Requests).ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Address_Fields_To_Provisioning()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, reader, _, application, _) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with
            {
                AddressLine1 = "1 High Street",
                AddressLine2 = "Flat 2",
                City = "London",
                County = "Greater London",
                PostCode = "SW1A 1AA",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var provisioned = Assert.Single(provisioning.Requests);
        Assert.Equal("1 High Street", provisioned.AddressLine1);
        Assert.Equal("Flat 2", provisioned.AddressLine2);
        Assert.Equal("London", provisioned.City);
        Assert.Equal("Greater London", provisioned.County);
        Assert.Equal("SW1A 1AA", provisioned.PostCode);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Hire_When_Provisioning_Rejects_The_Manager()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService(
            Result.Failure<Guid>(Error.NotFound("Manager employee was not found.")));
        var (_, companyId, vacancy, reader, _, application, stages) = SeedOfferStageApplication(db);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id) with { OverrideManager = true, ManagerId = Guid.NewGuid() },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        db.ChangeTracker.Clear();
        Assert.Equal(stages.Offer.Id, (await db.Applications.SingleAsync()).CurrentStageId);
        Assert.Null((await db.Candidates.SingleAsync()).EmployeeId);
    }

    private static HireCandidateHandler handler(
        RecruitmentDbContext db,
        FakeEmployeeProvisioningService? provisioning = null,
        FakeIntegrationEventPublisher? eventPublisher = null,
        FakePositionProfileReader? positionProfileReader = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            db,
            provisioning ?? new FakeEmployeeProvisioningService(),
            positionProfileReader ?? new FakePositionProfileReader(),
            new FakeClock(FixedUtcNow),
            eventPublisher ?? new FakeIntegrationEventPublisher(),
            auditPublisher ?? new FakeAuditPublisher(),
            new RecruitmentStageChangeRecorder(db, eventPublisher ?? new FakeIntegrationEventPublisher(), auditPublisher ?? new FakeAuditPublisher()));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
