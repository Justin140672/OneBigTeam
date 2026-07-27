using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.HireCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
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

    private static (RecruitmentDbContext db, Guid companyId, Vacancy vacancy, Guid departmentId, Guid locationId, FakePositionProfileReader reader)
        SeedVacancyWithResolvableProfile(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Software Engineer", null, Guid.NewGuid(), Now);

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Senior Software Engineer", departmentId, null, true, locationId, "London"),
        };

        return (db, companyId, vacancy, departmentId, locationId, new FakePositionProfileReader(summaries: summaries));
    }

    [Fact]
    public async Task HandleAsync_Hires_Offered_Application_And_Provisions_Employee()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var (_, companyId, vacancy, departmentId, locationId, reader) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", "+44 7700 900001", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, provisioning, positionProfileReader: reader, auditPublisher: auditPublisher).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Hired, result.Value!.Status);
        Assert.NotEqual(Guid.Empty, result.Value.EmployeeId);

        var savedCandidate = await db.Candidates.SingleAsync();
        Assert.Equal(result.Value.EmployeeId, savedCandidate.EmployeeId);

        var request = Assert.Single(provisioning.Requests);
        Assert.Equal("Emma", request.FirstName);
        Assert.Equal("Clarke", request.LastName);
        Assert.Equal("emma.clarke@example.com", request.WorkEmail);
        Assert.Equal("+44 7700 900001", request.PhoneNumber);
        // The employee's Department/Location/PositionProfile are derived exclusively from the
        // Vacancy's linked Position Profile — HireCandidateRequest carries no such client input.
        Assert.Equal(departmentId, request.DepartmentId);
        Assert.Equal(locationId, request.LocationId);
        Assert.Equal(vacancy.PositionProfileId, request.PositionProfileId);

        // Two audit events are published on hire: CandidateHiredAuditEvent (this handler's own) and
        // ApplicationStageChangedAuditEvent (RecruitmentStageChangeRecorder, ticket #67).
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
        var (_, companyId, vacancy, _, _, reader) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", "+44 7700 900001", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, eventPublisher, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Two integration events are published on hire: CandidateHiredIntegrationEvent (this
        // handler's own) and ApplicantStageChangedIntegrationEvent (RecruitmentStageChangeRecorder,
        // ticket #65).
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
    public async Task HandleAsync_Does_Not_Publish_Event_When_Application_Not_Offered()
    {
        await using var db = BuildContext();
        var eventPublisher = new FakeIntegrationEventPublisher();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, eventPublisher: eventPublisher, auditPublisher: auditPublisher).HandleAsync(
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
    public async Task HandleAsync_Returns_Validation_Error_When_Not_Offered()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Linked_To_Employee()
    {
        await using var db = BuildContext();
        var (_, companyId, vacancy, _, _, reader) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        candidate.LinkToEmployee(Guid.NewGuid(), Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
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
        var (_, companyId, vacancy, _, _, reader) = SeedVacancyWithResolvableProfile(db);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Noah", "Patel", "noah.patel@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, provisioning, positionProfileReader: reader).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(ApplicationStatus.Offered, savedApplication.Status);

        var savedCandidate = await db.Candidates.SingleAsync();
        Assert.Null(savedCandidate.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Ivy", "Wren", "ivy.wren@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Zara", "Osei", "zara.osei@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        // No summaries dictionary supplied — the linked position profile cannot be resolved.
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Milo", "Adeyemi", "milo.adeyemi@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Backend Engineer", null, null, true, Guid.NewGuid(), "London"),
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Nadia", "Farouk", "nadia.farouk@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Backend Engineer", Guid.NewGuid(), null, true, null, null),
        };

        var result = await handler(db, positionProfileReader: new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
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
