using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateLocation;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2 (optimistic concurrency rollout): Location.Version coverage for the UpdateLocation slice.
public class UpdateLocationConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static UpdateLocationHandler BuildHandler(EmployeesDbContext ctx)
        => new(ctx, new FakeClock(FixedUtcNow));

    private static async Task<(string DbName, Guid CompanyId, Guid Id, Guid LocationTypeId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        await using var seed = new EmployeesDbContext(Options(dbName));
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Original", null, now);
        seed.LocationTypes.Add(locationType);
        seed.Locations.Add(location);
        await seed.SaveChangesAsync();

        return (dbName, companyId, location.Id, locationType.Id);
    }

    private static UpdateLocationRequest Request(
        Guid companyId, Guid id, Guid locationTypeId, int? expectedVersion, string name)
        => new()
        {
            CompanyId = companyId,
            Id = id,
            Name = name,
            LocationTypeId = locationTypeId,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var location = Location.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "L", null, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        Assert.Equal(1, location.Version);

        location.IncrementVersion();
        Assert.Equal(2, location.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, id, locationTypeId) = await SeedAsync();

        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx).HandleAsync(
            Request(companyId, id, locationTypeId, expectedVersion: 1, name: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("Renamed", result.Value.Name);

        await using var verify = new EmployeesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.Locations.SingleAsync()).Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Preserves_Committed_Values()
    {
        var (dbName, companyId, id, locationTypeId) = await SeedAsync();

        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.Locations.SingleAsync();

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await BuildHandler(ctxB).HandleAsync(
                Request(companyId, id, locationTypeId, expectedVersion: 1, name: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await BuildHandler(ctxA).HandleAsync(
            Request(companyId, id, locationTypeId, expectedVersion: 1, name: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Locations.SingleAsync();
        Assert.Equal("Winner", saved.Name);
        Assert.Equal(2, saved.Version);
    }
}
