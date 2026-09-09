using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

// Ticket 2 (optimistic concurrency rollout): Vacancy.Version coverage for UpdateVacancy. See
// UpdateCandidateConcurrencyHandlerTests for the two-context InMemory approach. On the stale path the
// handler must not publish its VacancyUpdatedAuditEvent.
public class UpdateVacancyConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid VacancyId, Guid HiringManagerId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        await using var seed = new RecruitmentDbContext(Options(dbName));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, hiringManagerId, Now);
        seed.Vacancies.Add(vacancy);
        await seed.SaveChangesAsync();
        return (dbName, companyId, vacancy.Id, hiringManagerId);
    }

    private static UpdateVacancyRequest Request(Guid companyId, Guid vacancyId, Guid hiringManagerId, int? expectedVersion, string title)
        => new()
        {
            CompanyId = companyId,
            VacancyId = vacancyId,
            AdvertTitle = title,
            HiringManagerId = hiringManagerId,
            ExpectedVersion = expectedVersion,
        };

    private static UpdateVacancyHandler Handler(RecruitmentDbContext db, FakeAuditPublisher audit)
        => new(db, new FakeClock(FixedUtcNow), audit, new FakePositionProfileReader());

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, vacancyId, hm) = await SeedAsync();

        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, new FakeAuditPublisher())
            .HandleAsync(Request(companyId, vacancyId, hm, expectedVersion: 1, title: "Renamed"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        Assert.Equal(2, (await verify.Vacancies.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, vacancyId, hm) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(dbName));
        var result = await Handler(db, audit)
            .HandleAsync(Request(companyId, vacancyId, hm, expectedVersion: null, title: "Second"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.Vacancies.SingleAsync();
        Assert.Equal("Old Title", saved.AdvertTitle);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, vacancyId, hm) = await SeedAsync();

        await using var ctxA = new RecruitmentDbContext(Options(dbName));
        await ctxA.Vacancies.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB, new FakeAuditPublisher())
                .HandleAsync(Request(companyId, vacancyId, hm, expectedVersion: 1, title: "Winner"), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var result = await Handler(ctxA, audit)
            .HandleAsync(Request(companyId, vacancyId, hm, expectedVersion: 1, title: "Loser"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(dbName));
        var saved = await verify.Vacancies.SingleAsync();
        Assert.Equal("Winner", saved.AdvertTitle);
        Assert.Equal(2, saved.Version);
    }
}
