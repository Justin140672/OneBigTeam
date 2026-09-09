using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

// Ticket 2 (optimistic concurrency rollout): Candidate.Version coverage for UpdateCandidate. Two
// DbContext instances over the same EF InMemory database; context B saves first (bumping the store's
// Version), then the handler under test saves against context A with a stale ExpectedVersion,
// raising DbUpdateConcurrencyException exactly as real Postgres would. On the stale path the handler
// must not publish its CandidateUpdatedAuditEvent.
public class UpdateCandidateConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid CandidateId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        await using var seed = new RecruitmentDbContext(Options(dbName));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        seed.Candidates.Add(candidate);
        await seed.SaveChangesAsync();
        return (dbName, companyId, candidate.Id);
    }

    private static UpdateCandidateRequest Request(Guid companyId, Guid candidateId, int? expectedVersion, string lastName = "Clarke")
        => new()
        {
            CompanyId = companyId,
            CandidateId = candidateId,
            FirstName = "Emma",
            LastName = lastName,
            Email = "emma.clarke@example.com",
            ExpectedVersion = expectedVersion,
        };

    private static UpdateCandidateHandler Handler(RecruitmentDbContext db, FakeAuditPublisher audit)
        => new(db, new FakeClock(FixedUtcNow), audit);

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, candidateId) = await SeedAsync();

        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, new FakeAuditPublisher())
            .HandleAsync(Request(companyId, candidateId, expectedVersion: 1, lastName: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        Assert.Equal(2, (await verify.Candidates.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, candidateId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, audit)
            .HandleAsync(Request(companyId, candidateId, expectedVersion: null, lastName: "Second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.Candidates.SingleAsync();
        Assert.Equal("Clarke", saved.LastName);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, candidateId) = await SeedAsync();

        await using var ctxA = new RecruitmentDbContext(Options(dbName));
        await ctxA.Candidates.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB, new FakeAuditPublisher())
                .HandleAsync(Request(companyId, candidateId, expectedVersion: 1, lastName: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var result = await Handler(ctxA, audit)
            .HandleAsync(Request(companyId, candidateId, expectedVersion: 1, lastName: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.Candidates.SingleAsync();
        Assert.Equal("Winner", saved.LastName);
        Assert.Equal(2, saved.Version);
    }
}
