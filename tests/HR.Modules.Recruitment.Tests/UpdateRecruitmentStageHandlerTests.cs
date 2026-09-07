using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateRecruitmentStage;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class UpdateRecruitmentStageHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Active_Stage_To_Outcome_Held_By_Another_Active_Stage_Fails_Validation()
    {
        var store = NewStore();
        await using var db = BuildContext(store);
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        // "Offer" is a non-terminal active stage; try to make it terminal Hired while "Hired" already is.
        var result = await Handler(db).HandleAsync(
            new UpdateRecruitmentStageRequest(companyId, stages.Offer.Id, "Offer", true, RecruitmentStageTerminalOutcome.Hired),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejected_Terminal_Update_Does_Not_Partially_Persist()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        Guid targetId;

        await using (var db = BuildContext(store))
        {
            var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
            await db.SaveChangesAsync();
            targetId = stages.Offer.Id;

            var result = await Handler(db).HandleAsync(
                new UpdateRecruitmentStageRequest(companyId, targetId, "Offer", true, RecruitmentStageTerminalOutcome.Rejected),
                CancellationToken.None);

            Assert.True(result.IsFailure);
        }

        await using var fresh = BuildContext(store);
        var reloaded = await fresh.RecruitmentStages.SingleAsync(s => s.Id == targetId);
        Assert.False(reloaded.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.None, reloaded.TerminalOutcome);
    }

    [Fact]
    public async Task HandleAsync_Resaving_Stage_With_Its_Own_Outcome_Succeeds()
    {
        var store = NewStore();
        await using var db = BuildContext(store);
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateRecruitmentStageRequest(companyId, stages.Hired.Id, "Hired", true, RecruitmentStageTerminalOutcome.Hired),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecruitmentStageTerminalOutcome.Hired, result.Value!.TerminalOutcome);
    }

    [Fact]
    public async Task HandleAsync_Conflicting_Outcome_On_Inactive_Stage_Does_Not_Block()
    {
        var store = NewStore();
        await using var db = BuildContext(store);
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        stages.Hired.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateRecruitmentStageRequest(companyId, stages.Offer.Id, "Offer", true, RecruitmentStageTerminalOutcome.Hired),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecruitmentStageTerminalOutcome.Hired, result.Value!.TerminalOutcome);
    }

    [Fact]
    public async Task HandleAsync_Updating_Inactive_Stage_To_Duplicate_Outcome_Is_Allowed()
    {
        var store = NewStore();
        await using var db = BuildContext(store);
        var companyId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        // Loaded stage itself is inactive, so the terminal-uniqueness guard is skipped entirely.
        var extra = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Archived Outcome", 7, false, RecruitmentStageTerminalOutcome.None, Now);
        extra.SetActiveStatus(false, Now);
        db.RecruitmentStages.Add(extra);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateRecruitmentStageRequest(companyId, extra.Id, "Archived Outcome", true, RecruitmentStageTerminalOutcome.Hired),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecruitmentStageTerminalOutcome.Hired, result.Value!.TerminalOutcome);
    }

    [Fact]
    public async Task HandleAsync_Same_Outcome_In_Another_Company_Does_Not_Conflict()
    {
        var store = NewStore();
        await using var db = BuildContext(store);
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateRecruitmentStageRequest(companyId, stages.Hired.Id, "Hired", true, RecruitmentStageTerminalOutcome.Hired),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Successful_Update_Persists_And_Publishes_One_Audit_Event_With_Before_And_After()
    {
        var store = NewStore();
        var companyId = Guid.NewGuid();
        Guid targetId;
        var audit = new FakeAuditPublisher();

        await using (var db = BuildContext(store))
        {
            var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
            await db.SaveChangesAsync();
            targetId = stages.CvReview.Id;

            var result = await Handler(db, audit).HandleAsync(
                new UpdateRecruitmentStageRequest(companyId, targetId, "Screening", false, RecruitmentStageTerminalOutcome.None, RecruitmentStagePurpose.Interview),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        await using var fresh = BuildContext(store);
        var reloaded = await fresh.RecruitmentStages.SingleAsync(s => s.Id == targetId);
        Assert.Equal("Screening", reloaded.Name);
        Assert.Equal(RecruitmentStagePurpose.Interview, reloaded.Purpose);

        var evt = Assert.Single(audit.Published);
        var updated = Assert.IsType<RecruitmentStageUpdatedAuditEvent>(evt);
        Assert.Equal(companyId, updated.CompanyId);
        Assert.Equal(targetId, updated.RecruitmentStageId);
        Assert.Equal("CV Review", updated.Before.Name);
        Assert.Null(updated.Before.Purpose);
        Assert.Equal("Screening", updated.After.Name);
        Assert.Equal(RecruitmentStagePurpose.Interview, updated.After.Purpose);
        Assert.Equal(Now, updated.OccurredAt);
    }

    private static UpdateRecruitmentStageHandler Handler(RecruitmentDbContext db, FakeAuditPublisher? audit = null) =>
        new(db, new FakeClock(FixedUtcNow), audit ?? new FakeAuditPublisher());

    private static string NewStore() => Guid.NewGuid().ToString("N");

    private static RecruitmentDbContext BuildContext(string store) =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(store)
            .Options);
}
