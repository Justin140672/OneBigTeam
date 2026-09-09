using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompany;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

// Ticket 2 (optimistic concurrency rollout): Company.Version coverage for UpdateCompany. Two
// DbContext instances over the same EF InMemory database; context B saves first (bumping the
// store's Version via IncrementVersion), then the handler under test saves against context A with
// a stale ExpectedVersion, raising DbUpdateConcurrencyException exactly as real Postgres would.
// UpdateCompany publishes no audit/integration events, so the stale path only asserts nothing was
// committed. Mirrors HR.Modules.Assets.Tests/UpdateAssetConcurrencyHandlerTests.
public class UpdateCompanyConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<CompaniesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<CompaniesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static UpdateCompanyHandler Handler(CompaniesDbContext ctx)
        => new(ctx, new FakeClock(FixedUtcNow));

    private static async Task<(string DbName, Guid CompanyId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        await using var seed = new CompaniesDbContext(Options(dbName));
        var company = Company.Create(companyId, "Acme", now);
        company.SetAddress(CompanyAddress.Create(
            Guid.NewGuid(), companyId, CompanyAddressType.RegisteredOffice,
            "1 Old Street", null, "London", null, "SW1A 1AA", "GB", now), now);
        seed.Companies.Add(company);
        await seed.SaveChangesAsync();

        return (dbName, companyId);
    }

    private static UpdateCompanyRequest Request(Guid companyId, int? expectedVersion, string name = "Acme")
        => new()
        {
            CompanyId = companyId,
            Name = name,
            ExpectedVersion = expectedVersion,
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "10 High Street",
                    City = "London",
                    PostalCode = "SW1A 1AA",
                    CountryCode = "GB",
                }
            ],
        };

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId) = await SeedAsync();

        await using var db = new CompaniesDbContext(Options(dbName));
        var result = await Handler(db).HandleAsync(
            Request(companyId, expectedVersion: 1, name: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("Renamed", result.Value.Name);

        await using var verify = new CompaniesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.Companies.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId) = await SeedAsync();

        await using var db = new CompaniesDbContext(Options(dbName));
        var result = await Handler(db).HandleAsync(
            Request(companyId, expectedVersion: null, name: "Second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new CompaniesDbContext(Options(dbName));
        var saved = await verify.Companies.SingleAsync();
        Assert.Equal("Acme", saved.Name);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var (dbName, companyId) = await SeedAsync();

        await using var ctxA = new CompaniesDbContext(Options(dbName));
        await ctxA.Companies.SingleAsync();

        await using (var ctxB = new CompaniesDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB).HandleAsync(
                Request(companyId, expectedVersion: 1, name: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await Handler(ctxA).HandleAsync(
            Request(companyId, expectedVersion: 1, name: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new CompaniesDbContext(Options(dbName));
        var saved = await verify.Companies.SingleAsync();
        Assert.Equal("Winner", saved.Name);
        Assert.Equal(2, saved.Version);
    }
}
