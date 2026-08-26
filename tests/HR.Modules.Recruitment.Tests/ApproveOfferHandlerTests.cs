using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ApproveOffer;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ApproveOfferHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Approves_Offer_Successfully()
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

        var auditPublisher = new FakeAuditPublisher();
        var approvedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            approvedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(approvedBy, result.Value!.OfferApprovedByUserId);
        Assert.NotNull(result.Value.OfferApprovedAt);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(approvedBy, saved.OfferApprovedByUserId);
        Assert.NotNull(saved.OfferApprovedAt);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<OfferApprovedAuditEvent>(published);
        Assert.Equal(application.Id, auditEvent.ApplicationId);
        Assert.Equal(approvedBy, auditEvent.ApprovedByUserId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveOfferRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Application_Withdrawn()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    private static ApproveOfferHandler handler(
        RecruitmentDbContext db,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
