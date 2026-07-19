using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ImportLookupResolverTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    [Fact]
    public async Task GetOrCreateDepartmentAsync_Creates_New_Department_When_None_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await resolver.GetOrCreateDepartmentAsync(companyId, "Sales", CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.NotEqual(Guid.Empty, result.Id);

        var saved = await db.Departments.SingleAsync();
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal("Sales", saved.Name);
        Assert.Equal(companyId, saved.CompanyId);
    }

    [Fact]
    public async Task GetOrCreateDepartmentAsync_Reuses_Existing_Department_Case_And_Whitespace_Insensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var first = await resolver.GetOrCreateDepartmentAsync(companyId, "Sales", CancellationToken.None);
        var second = await resolver.GetOrCreateDepartmentAsync(companyId, "  SALES  ", CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Id, second.Id);

        Assert.Single(await db.Departments.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreateEmploymentTypeAsync_Creates_New_EmploymentType_When_None_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await resolver.GetOrCreateEmploymentTypeAsync(companyId, "Contractor", CancellationToken.None);

        Assert.True(result.WasCreated);

        var saved = await db.EmploymentTypes.SingleAsync();
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal("Contractor", saved.Name);
    }

    [Fact]
    public async Task GetOrCreateEmploymentTypeAsync_Reuses_Existing_EmploymentType_Case_And_Whitespace_Insensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var first = await resolver.GetOrCreateEmploymentTypeAsync(companyId, "Contractor", CancellationToken.None);
        var second = await resolver.GetOrCreateEmploymentTypeAsync(companyId, " contractor ", CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Id, second.Id);

        Assert.Single(await db.EmploymentTypes.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreateLocationAsync_Creates_Location_And_Auto_Creates_General_LocationType()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await resolver.GetOrCreateLocationAsync(companyId, "London", CancellationToken.None);

        Assert.True(result.WasCreated);

        var savedLocation = await db.Locations.SingleAsync();
        Assert.Equal(result.Id, savedLocation.Id);
        Assert.Equal("London", savedLocation.Name);
        Assert.Equal(companyId, savedLocation.CompanyId);

        var savedLocationType = await db.LocationTypes.SingleAsync();
        Assert.Equal(savedLocation.LocationTypeId, savedLocationType.Id);
        Assert.Equal("General", savedLocationType.Name);
        Assert.Equal(companyId, savedLocationType.CompanyId);
    }

    [Fact]
    public async Task GetOrCreateLocationAsync_Reuses_General_LocationType_Across_Multiple_Locations()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var first = await resolver.GetOrCreateLocationAsync(companyId, "London", CancellationToken.None);
        var second = await resolver.GetOrCreateLocationAsync(companyId, "Manchester", CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.True(second.WasCreated);
        Assert.NotEqual(first.Id, second.Id);

        Assert.Single(await db.LocationTypes.ToListAsync());

        var locations = await db.Locations.ToListAsync();
        Assert.Equal(2, locations.Count);
        Assert.All(locations, l => Assert.Equal(locations[0].LocationTypeId, l.LocationTypeId));
    }

    [Fact]
    public async Task GetOrCreateLocationAsync_Reuses_Preexisting_General_LocationType_Instead_Of_Duplicating()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var existingGeneral = LocationType.Create(Guid.NewGuid(), companyId, "General", "Seeded default", Now);
        db.LocationTypes.Add(existingGeneral);
        await db.SaveChangesAsync();

        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await resolver.GetOrCreateLocationAsync(companyId, "London", CancellationToken.None);

        Assert.True(result.WasCreated);

        var savedLocation = await db.Locations.SingleAsync();
        Assert.Equal(existingGeneral.Id, savedLocation.LocationTypeId);

        Assert.Single(await db.LocationTypes.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreatePositionProfileAsync_Returns_Existing_Profile_By_Title_Case_Insensitively_Regardless_Of_Department_Or_Location()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var existingProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Software Developer",
            null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        db.PositionProfiles.Add(existingProfile);
        await db.SaveChangesAsync();

        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await resolver.GetOrCreatePositionProfileAsync(
            companyId, "  SOFTWARE DEVELOPER  ", departmentId: Guid.NewGuid(), locationId: Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.WasCreated);
        Assert.False(result.Skipped);
        Assert.Equal(existingProfile.Id, result.Id);

        Assert.Single(await db.PositionProfiles.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreatePositionProfileAsync_Creates_Profile_When_Department_Location_And_DefaultLeavePolicy_All_Resolved()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var defaultLeavePolicyId = Guid.NewGuid();
        var resolver = new ImportLookupResolver(
            db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(defaultLeavePolicyId: defaultLeavePolicyId));

        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var result = await resolver.GetOrCreatePositionProfileAsync(
            companyId, "Software Developer", departmentId, locationId, CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.False(result.Skipped);
        Assert.NotNull(result.Id);

        var saved = await db.PositionProfiles.SingleAsync();
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal("Software Developer", saved.Title);
        Assert.Equal(departmentId, saved.DepartmentId);
        Assert.Equal(locationId, saved.LocationId);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task GetOrCreatePositionProfileAsync_Skips_When_Department_Location_Or_DefaultLeavePolicy_Missing(
        bool hasDepartment, bool hasLocation, bool hasDefaultLeavePolicy)
    {
        // PositionProfile requires a mandatory DefaultLeavePolicyId in addition to Department and Location.
        // The resolver can only auto-create a PositionProfile during import when all three are resolvable;
        // otherwise it skips and callers must create the position profile up front via CreatePositionProfile.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var defaultLeavePolicyId = hasDefaultLeavePolicy ? Guid.NewGuid() : (Guid?)null;
        var resolver = new ImportLookupResolver(
            db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(defaultLeavePolicyId: defaultLeavePolicyId));

        var departmentId = hasDepartment ? Guid.NewGuid() : (Guid?)null;
        var locationId = hasLocation ? Guid.NewGuid() : (Guid?)null;

        var result = await resolver.GetOrCreatePositionProfileAsync(
            companyId, "Software Developer", departmentId, locationId, CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.Null(result.Id);
        Assert.False(result.WasCreated);

        Assert.Empty(await db.PositionProfiles.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreateDepartmentAsync_Does_Not_Leak_Across_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var forCompanyA = await resolver.GetOrCreateDepartmentAsync(companyA, "Sales", CancellationToken.None);
        var forCompanyB = await resolver.GetOrCreateDepartmentAsync(companyB, "Sales", CancellationToken.None);

        Assert.True(forCompanyA.WasCreated);
        Assert.True(forCompanyB.WasCreated);
        Assert.NotEqual(forCompanyA.Id, forCompanyB.Id);

        Assert.Equal(2, await db.Departments.CountAsync());
    }

    [Fact]
    public async Task GetOrCreateEmploymentTypeAsync_Does_Not_Leak_Across_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var forCompanyA = await resolver.GetOrCreateEmploymentTypeAsync(companyA, "Contractor", CancellationToken.None);
        var forCompanyB = await resolver.GetOrCreateEmploymentTypeAsync(companyB, "Contractor", CancellationToken.None);

        Assert.True(forCompanyA.WasCreated);
        Assert.True(forCompanyB.WasCreated);
        Assert.NotEqual(forCompanyA.Id, forCompanyB.Id);

        Assert.Equal(2, await db.EmploymentTypes.CountAsync());
    }

    [Fact]
    public async Task GetOrCreateLocationAsync_Does_Not_Leak_Across_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var forCompanyA = await resolver.GetOrCreateLocationAsync(companyA, "London", CancellationToken.None);
        var forCompanyB = await resolver.GetOrCreateLocationAsync(companyB, "London", CancellationToken.None);

        Assert.True(forCompanyA.WasCreated);
        Assert.True(forCompanyB.WasCreated);
        Assert.NotEqual(forCompanyA.Id, forCompanyB.Id);

        Assert.Equal(2, await db.Locations.CountAsync());
        Assert.Equal(2, await db.LocationTypes.CountAsync());
    }

    [Fact]
    public async Task GetOrCreatePositionProfileAsync_Does_Not_Match_Existing_Profile_From_Different_Company()
    {
        // Since GetOrCreatePositionProfileAsync always skips instead of creating (see the
        // Always_Returns_Skipped test above), this test now verifies company isolation on the
        // lookup side only: a title match in another company must not be returned as "existing".
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var existingInCompanyA = PositionProfile.Create(
            Guid.NewGuid(), companyA, Guid.NewGuid(), Guid.NewGuid(), "Software Developer",
            null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        db.PositionProfiles.Add(existingInCompanyA);
        await db.SaveChangesAsync();

        var resolver = new ImportLookupResolver(db, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await resolver.GetOrCreatePositionProfileAsync(
            companyB, "Software Developer", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.False(result.WasCreated);
        Assert.Null(result.Id);
        Assert.NotEqual(existingInCompanyA.Id, result.Id);

        Assert.Single(await db.PositionProfiles.ToListAsync());
    }
}
