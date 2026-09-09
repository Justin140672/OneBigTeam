using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmploymentType;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2 (optimistic concurrency rollout): EmploymentType.Version coverage for the
// UpdateEmploymentType slice.
public class UpdateEmploymentTypeConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static UpdateEmploymentTypeHandler BuildHandler(EmployeesDbContext ctx)
        => new(ctx, new FakeClock(FixedUtcNow));

    private static async Task<(string DbName, Guid CompanyId, Guid Id)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        await using var seed = new EmployeesDbContext(Options(dbName));
        var entity = EmploymentType.Create(Guid.NewGuid(), companyId, "Original", null, now);
        seed.EmploymentTypes.Add(entity);
        await seed.SaveChangesAsync();

        return (dbName, companyId, entity.Id);
    }

    private static UpdateEmploymentTypeRequest Request(Guid companyId, Guid id, int? expectedVersion, string name)
        => new() { CompanyId = companyId, Id = id, Name = name, ExpectedVersion = expectedVersion };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var entity = EmploymentType.Create(
            Guid.NewGuid(), Guid.NewGuid(), "T", null, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        Assert.Equal(1, entity.Version);

        entity.IncrementVersion();
        Assert.Equal(2, entity.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx).HandleAsync(
            Request(companyId, id, expectedVersion: 1, name: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("Renamed", result.Value.Name);

        await using var verify = new EmployeesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.EmploymentTypes.SingleAsync()).Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Preserves_Committed_Values()
    {
        var (dbName, companyId, id) = await SeedAsync();

        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.EmploymentTypes.SingleAsync();

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await BuildHandler(ctxB).HandleAsync(
                Request(companyId, id, expectedVersion: 1, name: "Winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await BuildHandler(ctxA).HandleAsync(
            Request(companyId, id, expectedVersion: 1, name: "Loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.EmploymentTypes.SingleAsync();
        Assert.Equal("Winner", saved.Name);
        Assert.Equal(2, saved.Version);
    }
}
