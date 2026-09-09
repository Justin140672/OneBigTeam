using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdatePositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2 (optimistic concurrency rollout): PositionProfile.Version coverage for the
// UpdatePositionProfile slice. Mirrors EmployeeConcurrencyHandlerTests — two DbContext instances
// over the same EF InMemory database; context B saves first (bumping the store's Version), then the
// handler under test saves against context A with a stale ExpectedVersion.
public class UpdatePositionProfileConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static UpdatePositionProfileHandler BuildHandler(
        EmployeesDbContext ctx, FakeAuditPublisher audit, CapturingIntegrationEventPublisher integration)
        => new(ctx, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), audit, integration);

    private static async Task<(string DbName, Guid CompanyId, Guid DepartmentId, Guid LocationId, Guid ProfileId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        await using var seed = new EmployeesDbContext(Options(dbName));
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "London", null, now);
        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Original Title", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        seed.Departments.Add(department);
        seed.LocationTypes.Add(locationType);
        seed.Locations.Add(location);
        seed.PositionProfiles.Add(profile);
        await seed.SaveChangesAsync();

        return (dbName, companyId, department.Id, location.Id, profile.Id);
    }

    private static UpdatePositionProfileRequest Request(
        Guid companyId, Guid departmentId, Guid locationId, Guid profileId, int? expectedVersion, string title = "Original Title")
        => new()
        {
            CompanyId = companyId,
            Id = profileId,
            DepartmentId = departmentId,
            LocationId = locationId,
            DefaultLeavePolicyId = Guid.NewGuid(),
            Title = title,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var profile = PositionProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "T", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        Assert.Equal(1, profile.Version);

        profile.IncrementVersion();
        Assert.Equal(2, profile.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, departmentId, locationId, profileId) = await SeedAsync();

        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx, new FakeAuditPublisher(), new CapturingIntegrationEventPublisher())
            .HandleAsync(Request(companyId, departmentId, locationId, profileId, expectedVersion: 1, title: "Renamed"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("Renamed", result.Value.Title);

        await using var verify = new EmployeesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.PositionProfiles.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, departmentId, locationId, profileId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();
        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx, audit, integration)
            .HandleAsync(Request(companyId, departmentId, locationId, profileId, expectedVersion: null, title: "Second"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.PositionProfiles.SingleAsync();
        Assert.Equal("Original Title", saved.Title);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_Nothing()
    {
        var (dbName, companyId, departmentId, locationId, profileId) = await SeedAsync();

        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.PositionProfiles.SingleAsync();

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await BuildHandler(ctxB, new FakeAuditPublisher(), new CapturingIntegrationEventPublisher())
                .HandleAsync(Request(companyId, departmentId, locationId, profileId, expectedVersion: 1, title: "Winner"), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();
        var result = await BuildHandler(ctxA, audit, integration)
            .HandleAsync(Request(companyId, departmentId, locationId, profileId, expectedVersion: 1, title: "Loser"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.PositionProfiles.SingleAsync();
        Assert.Equal("Winner", saved.Title);
        Assert.Equal(2, saved.Version);
    }
}
