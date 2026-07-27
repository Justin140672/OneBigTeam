using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.WithdrawApplication;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class WithdrawApplicationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Withdraws_Application_In_Applied_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new WithdrawApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Withdrawn, result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Publishes_StageChanged_IntegrationEvent_And_AuditEvent_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();

        var result = await handler(db, eventPublisher, auditPublisher).HandleAsync(
            new WithdrawApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stageChanged = Assert.IsType<HR.SharedKernel.ApplicantStageChangedIntegrationEvent>(Assert.Single(eventPublisher.PublishedEvents));
        Assert.Equal(ApplicationStatus.Applied.ToString(), stageChanged.PreviousStage);
        Assert.Equal(ApplicationStatus.Withdrawn.ToString(), stageChanged.NewStage);
        Assert.Equal(performedBy, stageChanged.ChangedBy);

        var stageChangedAudit = Assert.IsType<ApplicationStageChangedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(ApplicationStatus.Applied, stageChangedAudit.PreviousStage);
        Assert.Equal(ApplicationStatus.Withdrawn, stageChangedAudit.NewStage);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Events_When_Already_Hired()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        application.Hire(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var eventPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, eventPublisher, auditPublisher).HandleAsync(
            new WithdrawApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
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
            new WithdrawApplicationRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_Hired()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Olivia", "Grant", "olivia.grant@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        application.Hire(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new WithdrawApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_Withdrawn()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "HR Business Partner", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Noah", "Patel", "noah.patel@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new WithdrawApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    private static WithdrawApplicationHandler handler(
        RecruitmentDbContext db,
        FakeIntegrationEventPublisher? eventPublisher = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db, new FakeClock(FixedUtcNow), new RecruitmentStageChangeRecorder(db, eventPublisher ?? new FakeIntegrationEventPublisher(), auditPublisher ?? new FakeAuditPublisher()));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
