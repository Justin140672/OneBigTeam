using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.HireCandidate;
using HR.Modules.Recruitment.Persistence;
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

    [Fact]
    public async Task HandleAsync_Hires_Offered_Application_And_Provisions_Employee()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
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

        var result = await handler(db, provisioning).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), CancellationToken.None);

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
    }

    [Fact]
    public async Task HandleAsync_Publishes_CandidateHiredIntegrationEvent()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService();
        var eventPublisher = new FakeIntegrationEventPublisher();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
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

        var result = await handler(db, provisioning, eventPublisher).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var publishedEvent = Assert.Single(eventPublisher.PublishedEvents);
        var candidateHired = Assert.IsType<CandidateHiredIntegrationEvent>(publishedEvent);
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db, eventPublisher: eventPublisher).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(eventPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Not_Offered()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Linked_To_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Product Designer", null, null, Guid.NewGuid(), Now);
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

        var result = await handler(db).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Hire_Application_When_Provisioning_Fails()
    {
        await using var db = BuildContext();
        var provisioning = new FakeEmployeeProvisioningService(Result.Failure<Guid>(Error.Conflict("Work email already exists.")));
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
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

        var result = await handler(db, provisioning).HandleAsync(
            BuildRequest(companyId, vacancy.Id, application.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal(ApplicationStatus.Offered, savedApplication.Status);

        var savedCandidate = await db.Candidates.SingleAsync();
        Assert.Null(savedCandidate.EmployeeId);
    }

    private static HireCandidateHandler handler(
        RecruitmentDbContext db,
        FakeEmployeeProvisioningService? provisioning = null,
        FakeIntegrationEventPublisher? eventPublisher = null) =>
        new(db, provisioning ?? new FakeEmployeeProvisioningService(), new FakeClock(FixedUtcNow), eventPublisher ?? new FakeIntegrationEventPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
