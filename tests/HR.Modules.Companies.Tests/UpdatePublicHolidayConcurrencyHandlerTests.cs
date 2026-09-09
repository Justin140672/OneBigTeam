using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdatePublicHoliday;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

// Ticket 2 (optimistic concurrency rollout): PublicHoliday.Version coverage for
// UpdatePublicHoliday. Two DbContext instances over the same EF InMemory database; context B saves
// first (bumping the store's Version), then the handler under test saves against context A with a
// stale ExpectedVersion, raising DbUpdateConcurrencyException exactly as real Postgres would.
// UpdatePublicHoliday publishes no audit/integration events, so the stale path only asserts
// nothing was committed. Mirrors HR.Modules.Assets.Tests/UpdateAssetConcurrencyHandlerTests.
public class UpdatePublicHolidayConcurrencyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<CompaniesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<CompaniesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static async Task<(string DbName, Guid CompanyId, Guid HolidayId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        await using var seed = new CompaniesDbContext(Options(dbName));
        var holiday = PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 12, 25), "Christmas Day", "GB", Now);
        seed.PublicHolidays.Add(holiday);
        await seed.SaveChangesAsync();

        return (dbName, companyId, holiday.Id);
    }

    private static UpdatePublicHolidayRequest Request(Guid companyId, Guid id, int? expectedVersion, string name = "Christmas Day")
        => new()
        {
            CompanyId = companyId,
            Id = id,
            Date = new DateOnly(2026, 12, 25),
            Name = name,
            CountryCode = "GB",
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, holidayId) = await SeedAsync();

        await using var db = new CompaniesDbContext(Options(dbName));
        var result = await new UpdatePublicHolidayHandler(db).HandleAsync(
            Request(companyId, holidayId, expectedVersion: 1, name: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("Renamed", result.Value.Name);

        await using var verify = new CompaniesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.PublicHolidays.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, holidayId) = await SeedAsync();

        await using var db = new CompaniesDbContext(Options(dbName));
        var result = await new UpdatePublicHolidayHandler(db).HandleAsync(
            Request(companyId, holidayId, expectedVersion: null, name: "Second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new CompaniesDbContext(Options(dbName));
        var saved = await verify.PublicHolidays.SingleAsync();
        Assert.Equal("Christmas Day", saved.Name);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var (dbName, companyId, holidayId) = await SeedAsync();

        await using var ctxA = new CompaniesDbContext(Options(dbName));
        await ctxA.PublicHolidays.SingleAsync();

        await using (var ctxB = new CompaniesDbContext(Options(dbName)))
        {
            var winner = await new UpdatePublicHolidayHandler(ctxB).HandleAsync(
                Request(companyId, holidayId, expectedVersion: 1, name: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await new UpdatePublicHolidayHandler(ctxA).HandleAsync(
            Request(companyId, holidayId, expectedVersion: 1, name: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new CompaniesDbContext(Options(dbName));
        var saved = await verify.PublicHolidays.SingleAsync();
        Assert.Equal("Winner", saved.Name);
        Assert.Equal(2, saved.Version);
    }
}
