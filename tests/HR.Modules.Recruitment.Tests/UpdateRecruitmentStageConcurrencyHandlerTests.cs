using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateRecruitmentStage;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

// Ticket 2 (optimistic concurrency rollout): RecruitmentStage.Version coverage for
// UpdateRecruitmentStage. See UpdateCandidateConcurrencyHandlerTests for the two-context InMemory
// approach. On the stale path the handler must not publish its RecruitmentStageUpdatedAuditEvent.
public class UpdateRecruitmentStageConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid StageId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        await using var seed = new RecruitmentDbContext(Options(dbName));
        var stage = RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review", 1, false, RecruitmentStageTerminalOutcome.None, Now);
        seed.RecruitmentStages.Add(stage);
        await seed.SaveChangesAsync();
        return (dbName, companyId, stage.Id);
    }

    private static UpdateRecruitmentStageRequest Request(Guid companyId, Guid stageId, int? expectedVersion, string name)
        => new(companyId, stageId, name, false, RecruitmentStageTerminalOutcome.None, null, expectedVersion);

    private static UpdateRecruitmentStageHandler Handler(RecruitmentDbContext db, FakeAuditPublisher audit)
        => new(db, new FakeClock(FixedUtcNow), audit);

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, stageId) = await SeedAsync();

        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, new FakeAuditPublisher())
            .HandleAsync(Request(companyId, stageId, expectedVersion: 1, name: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        Assert.Equal(2, (await verify.RecruitmentStages.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, stageId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, audit)
            .HandleAsync(Request(companyId, stageId, expectedVersion: null, name: "Second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.RecruitmentStages.SingleAsync();
        Assert.Equal("CV Review", saved.Name);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, stageId) = await SeedAsync();

        await using var ctxA = new RecruitmentDbContext(Options(dbName));
        await ctxA.RecruitmentStages.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB, new FakeAuditPublisher())
                .HandleAsync(Request(companyId, stageId, expectedVersion: 1, name: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var result = await Handler(ctxA, audit)
            .HandleAsync(Request(companyId, stageId, expectedVersion: 1, name: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.RecruitmentStages.SingleAsync();
        Assert.Equal("Winner", saved.Name);
        Assert.Equal(2, saved.Version);
    }
}
