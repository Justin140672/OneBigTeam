using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.AddInterviewStage;
using HR.Modules.Recruitment.Features.GetInterviewStageSuggestion;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class AddInterviewStageHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Seeder_Still_Creates_A_Single_Interview_Stage()
    {
        var stages = RecruitmentStageSeeder.BuildDefaultStages(Guid.NewGuid(), Now);

        Assert.Single(stages, s => s.Purpose == RecruitmentStagePurpose.Interview);
        Assert.Equal("Interview", stages.Single(s => s.Purpose == RecruitmentStagePurpose.Interview).Name);
    }

    [Fact]
    public async Task HandleAsync_Second_Stage_Defaults_And_Renames_Default_Interview()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Second Interview", result.Value!.Name);
        Assert.Equal(4, result.Value.DisplayOrder);
        Assert.True(result.Value.IsActive);
        Assert.Equal(seeded.Interview.Id, result.Value.RenamedStageId);

        await using var fresh = BuildContext(store);
        var all = await fresh.RecruitmentStages.Where(s => s.CompanyId == companyId).OrderBy(s => s.DisplayOrder).ToListAsync();
        Assert.Equal(
            ["Application Received", "CV Review", "First Interview", "Second Interview", "Offer", "Hired", "Rejected"],
            all.Select(s => s.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], all.Select(s => s.DisplayOrder));

        var added = all.Single(s => s.Name == "Second Interview");
        Assert.Equal(RecruitmentStagePurpose.Interview, added.Purpose);
        Assert.False(added.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.None, added.TerminalOutcome);
    }

    [Theory]
    [InlineData("  interview  ")]
    [InlineData("INTERVIEW")]
    public async Task HandleAsync_Renames_Default_Name_Ignoring_Case_And_Whitespace(string existingName)
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        seeded.Interview.UpdateDetails(existingName, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var fresh = BuildContext(store);
        Assert.Equal("First Interview", (await fresh.RecruitmentStages.SingleAsync(s => s.Id == seeded.Interview.Id)).Name);
    }

    [Theory]
    [InlineData("Technical Interview")]
    [InlineData("Hiring Manager Interview")]
    public async Task HandleAsync_Preserves_Custom_Names(string customName)
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        seeded.Interview.UpdateDetails(customName, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RenamedStageId);
        await using var fresh = BuildContext(store);
        Assert.Equal(customName, (await fresh.RecruitmentStages.SingleAsync(s => s.Id == seeded.Interview.Id)).Name);
    }

    [Fact]
    public async Task HandleAsync_Existing_First_Interview_Is_Not_Updated()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        seeded.Interview.UpdateDetails("First Interview", false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        await db.SaveChangesAsync();
        var versionBefore = seeded.Interview.Version;

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RenamedStageId);
        await using var fresh = BuildContext(store);
        var reloaded = await fresh.RecruitmentStages.SingleAsync(s => s.Id == seeded.Interview.Id);
        Assert.Equal("First Interview", reloaded.Name);
        Assert.Equal(versionBefore, reloaded.Version);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Rename_When_Adding_Third_Interview_Stage()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        await handler.HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);
        var third = await handler.HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        Assert.True(third.IsSuccess);
        Assert.Equal("Third Interview", third.Value!.Name);
        Assert.Equal(5, third.Value.DisplayOrder);
        Assert.Null(third.Value.RenamedStageId);

        await using var fresh = BuildContext(store);
        var all = await fresh.RecruitmentStages.Where(s => s.CompanyId == companyId).OrderBy(s => s.DisplayOrder).ToListAsync();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], all.Select(s => s.DisplayOrder));
        Assert.Equal("Offer", all[5].Name);
    }

    [Fact]
    public async Task HandleAsync_Accepts_Name_Override()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId, "  Panel Interview "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Panel Interview", result.Value!.Name);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Name_Fails_And_Persists_Nothing()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId, "Offer"), CancellationToken.None);

        Assert.True(result.IsFailure);
        await using var fresh = BuildContext(store);
        Assert.Equal(6, await fresh.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
        Assert.Equal("Interview", (await fresh.RecruitmentStages.SingleAsync(s => s.Id == seeded.Interview.Id)).Name);
    }

    [Fact]
    public async Task HandleAsync_Inactive_Interview_Stages_Are_Not_Counted()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var inactive = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Old Interview", 7, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        inactive.SetActiveStatus(false, Now);
        db.RecruitmentStages.Add(inactive);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Second Interview", result.Value!.Name);
        Assert.Equal(4, result.Value.DisplayOrder);
        Assert.Equal(seeded.Interview.Id, result.Value.RenamedStageId);
    }

    [Fact]
    public async Task HandleAsync_Ordinal_Follows_Interview_Stages_Not_Total_Stage_Count()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        seeded.Interview.UpdateDetails("Technical Interview", false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        db.RecruitmentStages.Add(RecruitmentStage.Create(Guid.NewGuid(), companyId, "Reference Check", 7, false, RecruitmentStageTerminalOutcome.None, Now));
        await db.SaveChangesAsync();

        var suggestion = await new GetInterviewStageSuggestionHandler(db)
            .HandleAsync(new GetInterviewStageSuggestionRequest(companyId), CancellationToken.None);

        Assert.Equal("Second Interview", suggestion.Value!.SuggestedName);
        Assert.Equal(4, suggestion.Value.DisplayOrder);
        Assert.Equal(1, suggestion.Value.ActiveInterviewStageCount);
        Assert.Null(suggestion.Value.StageToRenameName);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Touch_Other_Companies()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var other = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        await using var fresh = BuildContext(store);
        var otherStages = await fresh.RecruitmentStages.Where(s => s.CompanyId == otherCompanyId).OrderBy(s => s.DisplayOrder).ToListAsync();
        Assert.Equal(6, otherStages.Count);
        Assert.Equal("Interview", otherStages.Single(s => s.Id == other.Interview.Id).Name);
        Assert.Equal([1, 2, 3, 4, 5, 6], otherStages.Select(s => s.DisplayOrder));
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Move_Existing_Applications_Or_Interviews()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        await using var db = BuildContext(store);
        var seeded = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var applicationId = Guid.NewGuid();
        var interview = RecruitmentStageTestData.PassedInterview(companyId, applicationId, seeded.Interview.Id, Now);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(new AddInterviewStageRequest(companyId), CancellationToken.None);

        await using var fresh = BuildContext(store);
        Assert.Equal(seeded.Interview.Id, (await fresh.Interviews.SingleAsync(i => i.Id == interview.Id)).StageId);
    }

    private static AddInterviewStageHandler Handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static string NewStore() => Guid.NewGuid().ToString("N");

    private static RecruitmentDbContext BuildContext(string store) =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(store)
            .Options);
}
