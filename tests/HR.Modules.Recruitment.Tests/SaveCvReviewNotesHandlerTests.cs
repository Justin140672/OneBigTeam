using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.SaveCvReviewNotes;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class SaveCvReviewNotesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SaveCvReviewNotesHandler Handler(RecruitmentDbContext db, FakeAuditPublisher? audit = null) =>
        new(db, new FakeClock(FixedUtcNow), audit ?? new FakeAuditPublisher());

    private static async Task<(Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid StageId)> SeedAsync(
        RecruitmentDbContext db, bool withdrawn = false)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now);
        if (withdrawn)
            application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (companyId, vacancy.Id, application.Id, stages.CvReview.Id);
    }

    private static SaveCvReviewNotesRequest Request(Guid companyId, Guid vacancyId, Guid applicationId, string? notes) => new()
    {
        CompanyId     = companyId,
        VacancyId     = vacancyId,
        ApplicationId = applicationId,
        CvReviewNotes = notes,
    };

    [Fact]
    public async Task HandleAsync_Saves_And_Normalises_Notes_And_Stamps_Reviewer()
    {
        await using var db = BuildContext();
        var audit = new FakeAuditPublisher();
        var (companyId, vacancyId, applicationId, stageId) = await SeedAsync(db);
        var performedBy = Guid.NewGuid();

        var result = await Handler(db, audit).HandleAsync(
            Request(companyId, vacancyId, applicationId, "  Strong candidate  "), performedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Strong candidate", result.Value!.CvReviewNotes);
        Assert.NotNull(result.Value.CvReviewedAt);
        Assert.Equal(performedBy, result.Value.CvReviewedByUserId);
        Assert.Equal(stageId, result.Value.CurrentStageId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal("Strong candidate", saved.CvReviewNotes);
        Assert.Equal(performedBy, saved.CvReviewedByUserId);

        var evt = Assert.Single(audit.Published);
        var cvEvent = Assert.IsType<ApplicationCvReviewNotesSavedAuditEvent>(evt);
        Assert.Equal(companyId, cvEvent.CompanyId);
        Assert.Equal(applicationId, cvEvent.ApplicationId);
        Assert.Equal(vacancyId, cvEvent.VacancyId);
        Assert.Equal(performedBy, cvEvent.ReviewedByUserId);
        Assert.False(cvEvent.NotesCleared);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_Clearing_Notes_Sets_NotesCleared_True_And_Notes_Null(string? notes)
    {
        await using var db = BuildContext();
        var audit = new FakeAuditPublisher();
        var (companyId, vacancyId, applicationId, _) = await SeedAsync(db);

        var result = await Handler(db, audit).HandleAsync(
            Request(companyId, vacancyId, applicationId, notes), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvReviewNotes);

        var cvEvent = Assert.IsType<ApplicationCvReviewNotesSavedAuditEvent>(Assert.Single(audit.Published));
        Assert.True(cvEvent.NotesCleared);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Change_CurrentStageId()
    {
        await using var db = BuildContext();
        var (companyId, vacancyId, applicationId, stageId) = await SeedAsync(db);

        await Handler(db).HandleAsync(
            Request(companyId, vacancyId, applicationId, "notes"), Guid.NewGuid(), CancellationToken.None);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(stageId, saved.CurrentStageId);
        Assert.Empty(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "notes"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Mismatch()
    {
        await using var db = BuildContext();
        var (_, vacancyId, applicationId, _) = await SeedAsync(db);

        var result = await Handler(db).HandleAsync(
            Request(Guid.NewGuid(), vacancyId, applicationId, "notes"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Mismatch()
    {
        await using var db = BuildContext();
        var (companyId, _, applicationId, _) = await SeedAsync(db);

        var result = await Handler(db).HandleAsync(
            Request(companyId, Guid.NewGuid(), applicationId, "notes"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Withdrawn()
    {
        await using var db = BuildContext();
        var audit = new FakeAuditPublisher();
        var (companyId, vacancyId, applicationId, _) = await SeedAsync(db, withdrawn: true);

        var result = await Handler(db, audit).HandleAsync(
            Request(companyId, vacancyId, applicationId, "notes"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(audit.Published);
    }
}
