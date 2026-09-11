using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.MoveApplicationForward;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class MoveApplicationForwardHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static MoveApplicationForwardHandler Handler(
        RecruitmentDbContext db,
        FakeIntegrationEventPublisher? events = null,
        FakeAuditPublisher? audit = null) =>
        new(
            db,
            new FakeClock(FixedUtcNow),
            audit ?? new FakeAuditPublisher(),
            new RecruitmentStageChangeRecorder(db, events ?? new FakeIntegrationEventPublisher(), audit ?? new FakeAuditPublisher()));

    private sealed record Seed(
        Guid CompanyId, Guid VacancyId, Guid CandidateId, Guid ApplicationId,
        RecruitmentStageTestData.SeededStages Stages);

    private static async Task<Seed> SeedAsync(
        RecruitmentDbContext db, Func<RecruitmentStageTestData.SeededStages, RecruitmentStage> currentStage, bool withdrawn = false)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, currentStage(stages).Id, null, Now);
        if (withdrawn)
            application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return new Seed(companyId, vacancy.Id, candidate.Id, application.Id, stages);
    }

    private static MoveApplicationForwardRequest Request(Seed seed, string? notes = null) => new()
    {
        CompanyId     = seed.CompanyId,
        VacancyId     = seed.VacancyId,
        ApplicationId = seed.ApplicationId,
        CvReviewNotes = notes,
    };

    [Fact]
    public async Task HandleAsync_Advances_To_Next_Active_NonTerminal_Stage_By_DisplayOrder()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, s => s.CvReview);

        var result = await Handler(db).HandleAsync(Request(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.Stages.CvReview.Id, result.Value!.PreviousStageId);
        Assert.Equal(seed.Stages.Interview.Id, result.Value.CurrentStageId);
        Assert.Equal("Interview", result.Value.CurrentStageName);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(seed.Stages.Interview.Id, saved.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Skips_Inactive_Intermediate_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, s => s.CvReview);
        var interview = await db.RecruitmentStages.SingleAsync(s => s.Id == seed.Stages.Interview.Id);
        interview.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(Request(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.Stages.Offer.Id, result.Value!.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Saves_Notes_And_Publishes_Cv_Audit_Event_When_Notes_Provided()
    {
        await using var db = BuildContext();
        var events = new FakeIntegrationEventPublisher();
        var audit = new FakeAuditPublisher();
        var seed = await SeedAsync(db, s => s.CvReview);
        var performedBy = Guid.NewGuid();

        var result = await Handler(db, events, audit).HandleAsync(
            Request(seed, "  Great fit  "), performedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.Applications.SingleAsync();
        Assert.Equal("Great fit", saved.CvReviewNotes);
        Assert.Equal(performedBy, saved.CvReviewedByUserId);

        Assert.Single(audit.Published.OfType<ApplicationCvReviewNotesSavedAuditEvent>());
        var cvEvent = audit.Published.OfType<ApplicationCvReviewNotesSavedAuditEvent>().Single();
        Assert.False(cvEvent.NotesCleared);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Notes_Untouched_And_Publishes_No_Cv_Audit_Event_When_Notes_Null()
    {
        await using var db = BuildContext();
        var audit = new FakeAuditPublisher();
        var seed = await SeedAsync(db, s => s.CvReview);

        var result = await Handler(db, audit: audit).HandleAsync(Request(seed, notes: null), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.Applications.SingleAsync();
        Assert.Null(saved.CvReviewNotes);
        Assert.Null(saved.CvReviewedAt);
        Assert.Empty(audit.Published.OfType<ApplicationCvReviewNotesSavedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Adds_Exactly_One_History_Entry_And_Publishes_Stage_Changed_Events()
    {
        await using var db = BuildContext();
        var events = new FakeIntegrationEventPublisher();
        var audit = new FakeAuditPublisher();
        var seed = await SeedAsync(db, s => s.CvReview);
        var performedBy = Guid.NewGuid();

        var result = await Handler(db, events, audit).HandleAsync(Request(seed), performedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var history = Assert.Single(await db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Equal(seed.Stages.CvReview.Id, history.PreviousStageId);
        Assert.Equal(seed.Stages.Interview.Id, history.NewStageId);

        var integrationEvent = Assert.Single(events.PublishedEvents);
        var stageChanged = Assert.IsType<ApplicationStageChangedIntegrationEvent>(integrationEvent);
        Assert.Equal("CV Review", stageChanged.PreviousStage);
        Assert.Equal("Interview", stageChanged.NewStage);

        var auditEvent = Assert.Single(audit.Published.OfType<ApplicationStageChangedAuditEvent>());
        Assert.Equal(seed.Stages.CvReview.Id, auditEvent.PreviousStageId);
        Assert.Equal(seed.Stages.Interview.Id, auditEvent.NewStageId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_And_No_Side_Effects_When_No_Active_Stage_After_Current()
    {
        await using var db = BuildContext();
        var events = new FakeIntegrationEventPublisher();
        var audit = new FakeAuditPublisher();
        // Offer is the last non-terminal stage — there is nothing active/non-terminal after it.
        var seed = await SeedAsync(db, s => s.Offer);

        var result = await Handler(db, events, audit).HandleAsync(Request(seed, "notes"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(seed.Stages.Offer.Id, saved.CurrentStageId);
        Assert.Null(saved.CvReviewNotes);
        Assert.Empty(await db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Empty(events.PublishedEvents);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Current_Stage_Terminal()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, s => s.Hired);

        var result = await Handler(db).HandleAsync(Request(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Withdrawn()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, s => s.CvReview, withdrawn: true);

        var result = await Handler(db).HandleAsync(Request(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new MoveApplicationForwardRequest
            {
                CompanyId = Guid.NewGuid(),
                VacancyId = Guid.NewGuid(),
                ApplicationId = Guid.NewGuid(),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Mismatch()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, s => s.CvReview);

        var result = await Handler(db).HandleAsync(
            Request(seed) with { CompanyId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
