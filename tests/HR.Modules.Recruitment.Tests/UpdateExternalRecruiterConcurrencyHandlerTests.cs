using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateExternalRecruiter;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

// Ticket 2 (optimistic concurrency rollout): ExternalRecruiter.Version coverage for
// UpdateExternalRecruiter. See UpdateCandidateConcurrencyHandlerTests for the two-context InMemory
// approach. On the stale path the handler must not publish its ExternalRecruiterUpdatedAuditEvent.
public class UpdateExternalRecruiterConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid RecruiterId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        await using var seed = new RecruitmentDbContext(Options(dbName));
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        seed.ExternalRecruiters.Add(recruiter);
        await seed.SaveChangesAsync();
        return (dbName, companyId, recruiter.Id);
    }

    private static UpdateExternalRecruiterRequest Request(Guid companyId, Guid recruiterId, int? expectedVersion, string agencyName)
        => new(companyId, recruiterId, agencyName, null, null, null, null, null, expectedVersion);

    private static UpdateExternalRecruiterHandler Handler(RecruitmentDbContext db, FakeAuditPublisher audit)
        => new(db, new FakeClock(FixedUtcNow), audit);

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, recruiterId) = await SeedAsync();

        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, new FakeAuditPublisher())
            .HandleAsync(Request(companyId, recruiterId, expectedVersion: 1, agencyName: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        Assert.Equal(2, (await verify.ExternalRecruiters.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, recruiterId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, audit)
            .HandleAsync(Request(companyId, recruiterId, expectedVersion: null, agencyName: "Second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.ExternalRecruiters.SingleAsync();
        Assert.Equal("Acme Recruiting", saved.AgencyName);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, recruiterId) = await SeedAsync();

        await using var ctxA = new RecruitmentDbContext(Options(dbName));
        await ctxA.ExternalRecruiters.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB, new FakeAuditPublisher())
                .HandleAsync(Request(companyId, recruiterId, expectedVersion: 1, agencyName: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var result = await Handler(ctxA, audit)
            .HandleAsync(Request(companyId, recruiterId, expectedVersion: 1, agencyName: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.ExternalRecruiters.SingleAsync();
        Assert.Equal("Winner", saved.AgencyName);
        Assert.Equal(2, saved.Version);
    }
}
