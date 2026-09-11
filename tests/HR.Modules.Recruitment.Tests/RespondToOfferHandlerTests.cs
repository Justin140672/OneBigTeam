using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RespondToOffer;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class RespondToOfferHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static (Guid companyId, Vacancy vacancy, Candidate candidate, Application application, RecruitmentStageTestData.SeededStages stages)
        Seed(RecruitmentDbContext db, bool withOffer = true, bool withdrawn = false)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Offer.Id, null, Now);
        if (withOffer)
            application.RecordOfferTerms(60000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 9, 1), new DateOnly(2026, 7, 4), null, Now);
        if (withdrawn)
            application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        return (companyId, vacancy, candidate, application, stages);
    }

    [Theory]
    [InlineData("Accepted", (int)OfferResponseStatus.Accepted)]
    [InlineData("Declined", (int)OfferResponseStatus.Declined)]
    [InlineData("Withdrawn", (int)OfferResponseStatus.Withdrawn)]
    public async Task HandleAsync_Records_Response_Sets_RespondedAt_Publishes_Audit_And_Leaves_Stage_Unchanged(
        string status, int expectedRaw)
    {
        var expected = (OfferResponseStatus)expectedRaw;
        await using var db = BuildContext();
        var (companyId, vacancy, candidate, application, stages) = Seed(db);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher).HandleAsync(
            new RespondToOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id, Status = status },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.ToString(), result.Value!.OfferResponseStatus);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(expected, saved.OfferResponseStatus);
        Assert.NotNull(saved.OfferRespondedAt);
        Assert.Equal(stages.Offer.Id, saved.CurrentStageId);

        var audit = Assert.IsType<OfferResponseRecordedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("offer.response_recorded", ((IAuditEvent)audit).EventType);
        Assert.Equal(application.Id, ((IAuditEvent)audit).EntityId);
        Assert.Equal(performedBy, ((IAuditEvent)audit).ActorUserId);
        Assert.Equal("AwaitingResponse", audit.PreviousStatus);
        Assert.Equal(expected.ToString(), audit.NewStatus);
        Assert.Equal(candidate.Id, audit.CandidateId);
    }

    [Fact]
    public async Task HandleAsync_Is_Case_Insensitive_On_Status()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, _, application, _) = Seed(db);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new RespondToOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id, Status = "accEPTed" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Accepted", result.Value!.OfferResponseStatus);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new RespondToOfferRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), Status = "Accepted" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_No_Offer_Has_Been_Made()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, _, application, _) = Seed(db, withOffer: false);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new RespondToOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id, Status = "Accepted" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("No offer has been made for this application yet.", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Application_Withdrawn()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, _, application, _) = Seed(db, withdrawn: true);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new RespondToOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id, Status = "Accepted" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Offer_Already_Resolved()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, _, application, _) = Seed(db);
        application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new RespondToOfferRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id, Status = "Declined" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(OfferResponseStatus.Accepted, saved.OfferResponseStatus);
    }

    private static RespondToOfferHandler handler(RecruitmentDbContext db, FakeAuditPublisher? auditPublisher = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
