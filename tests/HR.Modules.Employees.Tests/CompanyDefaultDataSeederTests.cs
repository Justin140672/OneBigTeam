using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CompanyDefaultDataSeederTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SeedDefaultsAsync_Creates_Exactly_One_Of_Each_Default_Entity()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leavePolicyProvisioner = new FakeLeavePolicyProvisioner();
        var seeder = new CompanyDefaultDataSeeder(
            context, new FakeClock(FixedUtcNow), leavePolicyProvisioner,
            new FakeLeaveTypeDefaultsProvisioner(), new FakeSicknessCategoryDefaultsProvisioner(),
            new FakeDocumentTypeDefaultsProvisioner());

        await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, await context.Departments.CountAsync(d => d.CompanyId == companyId));
        Assert.Equal(2, await context.LocationTypes.CountAsync(lt => lt.CompanyId == companyId));
        Assert.Equal(2, await context.Locations.CountAsync(l => l.CompanyId == companyId));
        Assert.Equal(5, await context.EmploymentTypes.CountAsync(et => et.CompanyId == companyId));
        Assert.Equal(1, await context.PositionProfiles.CountAsync(pp => pp.CompanyId == companyId));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Returns_Ids_That_Correspond_To_The_Created_Rows()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leavePolicyProvisioner = new FakeLeavePolicyProvisioner();
        var seeder = new CompanyDefaultDataSeeder(
            context, new FakeClock(FixedUtcNow), leavePolicyProvisioner,
            new FakeLeaveTypeDefaultsProvisioner(), new FakeSicknessCategoryDefaultsProvisioner(),
            new FakeDocumentTypeDefaultsProvisioner());

        var result = await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.DepartmentId);
        Assert.NotEqual(Guid.Empty, result.LocationId);
        Assert.NotEqual(Guid.Empty, result.PositionProfileId);
        Assert.NotEqual(Guid.Empty, result.EmploymentTypeId);

        var department = await context.Departments.SingleAsync(d => d.CompanyId == companyId);
        Assert.Equal(result.DepartmentId, department.Id);
        Assert.Equal("General", department.Name);

        var location = await context.Locations.SingleAsync(l => l.CompanyId == companyId && l.Name == "Head Office");
        Assert.Equal(result.LocationId, location.Id);

        var locationType = await context.LocationTypes.SingleAsync(lt => lt.CompanyId == companyId && lt.Name == "Office");
        Assert.Equal(locationType.Id, location.LocationTypeId);
        Assert.Equal("Office", locationType.Name);

        var employmentType = await context.EmploymentTypes.SingleAsync(et => et.Id == result.EmploymentTypeId);
        Assert.Equal("Permanent", employmentType.Name);

        var employmentTypeNames = await context.EmploymentTypes
            .Where(et => et.CompanyId == companyId).Select(et => et.Name).ToListAsync();
        Assert.Equal(
            new[] { "Permanent", "Fixed Term", "Contractor", "Casual", "Apprentice" }.OrderBy(n => n),
            employmentTypeNames.OrderBy(n => n));

        var positionProfile = await context.PositionProfiles.SingleAsync(pp => pp.CompanyId == companyId);
        Assert.Equal(result.PositionProfileId, positionProfile.Id);
        Assert.Equal("Administrator", positionProfile.Title);
    }

    [Fact]
    public async Task SeedDefaultsAsync_PositionProfile_References_Correct_Department_Location_And_LeavePolicy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leavePolicyProvisioner = new FakeLeavePolicyProvisioner
        {
            PolicyIdToReturn = Guid.NewGuid(),
        };
        var seeder = new CompanyDefaultDataSeeder(
            context, new FakeClock(FixedUtcNow), leavePolicyProvisioner,
            new FakeLeaveTypeDefaultsProvisioner(), new FakeSicknessCategoryDefaultsProvisioner(),
            new FakeDocumentTypeDefaultsProvisioner());

        var result = await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        var positionProfile = await context.PositionProfiles.SingleAsync(pp => pp.CompanyId == companyId);
        Assert.Equal(result.DepartmentId, positionProfile.DepartmentId);
        Assert.Equal(result.LocationId, positionProfile.LocationId);
        Assert.Equal(leavePolicyProvisioner.PolicyIdToReturn, positionProfile.DefaultLeavePolicyId);
    }

    [Fact]
    public async Task SeedDefaultsAsync_Calls_LeavePolicyProvisioner_With_The_Given_CompanyId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leavePolicyProvisioner = new FakeLeavePolicyProvisioner();
        var seeder = new CompanyDefaultDataSeeder(
            context, new FakeClock(FixedUtcNow), leavePolicyProvisioner,
            new FakeLeaveTypeDefaultsProvisioner(), new FakeSicknessCategoryDefaultsProvisioner(),
            new FakeDocumentTypeDefaultsProvisioner());

        await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, leavePolicyProvisioner.CallCount);
        Assert.Equal(companyId, Assert.Single(leavePolicyProvisioner.RequestedCompanyIds));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Calls_LeaveType_SicknessCategory_And_DocumentType_DefaultsProvisioners()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveTypeProvisioner = new FakeLeaveTypeDefaultsProvisioner();
        var sicknessCategoryProvisioner = new FakeSicknessCategoryDefaultsProvisioner();
        var documentTypeProvisioner = new FakeDocumentTypeDefaultsProvisioner();
        var seeder = new CompanyDefaultDataSeeder(
            context, new FakeClock(FixedUtcNow), new FakeLeavePolicyProvisioner(),
            leaveTypeProvisioner, sicknessCategoryProvisioner, documentTypeProvisioner);

        await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, leaveTypeProvisioner.CallCount);
        Assert.Equal(companyId, Assert.Single(leaveTypeProvisioner.RequestedCompanyIds));
        Assert.Equal(1, sicknessCategoryProvisioner.CallCount);
        Assert.Equal(companyId, Assert.Single(sicknessCategoryProvisioner.RequestedCompanyIds));
        Assert.Equal(1, documentTypeProvisioner.CallCount);
        Assert.Equal(companyId, Assert.Single(documentTypeProvisioner.RequestedCompanyIds));
    }

    private static CompanyDefaultDataSeeder BuildSeeder(EmployeesDbContext context) =>
        new(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyProvisioner(),
            new FakeLeaveTypeDefaultsProvisioner(), new FakeSicknessCategoryDefaultsProvisioner(),
            new FakeDocumentTypeDefaultsProvisioner());

    [Fact]
    public async Task SeedDefaultsAsync_Scaffolds_Home_Location_With_Remote_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        await BuildSeeder(context).SeedDefaultsAsync(companyId, CancellationToken.None);

        var home = await context.Locations.SingleAsync(l => l.CompanyId == companyId && l.Name == "Home");
        var type = await context.LocationTypes.SingleAsync(t => t.Id == home.LocationTypeId);
        Assert.Equal("Remote", type.Name);
        Assert.Equal(companyId, type.CompanyId);
        Assert.True(home.IsActive);
    }

    [Fact]
    public async Task SeedDefaultsAsync_Is_Idempotent_For_Locations_And_Types()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var seeder = BuildSeeder(context);

        var first = await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);
        var second = await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(first.LocationId, second.LocationId);
        Assert.Equal(2, await context.Locations.CountAsync(l => l.CompanyId == companyId));
        Assert.Equal(2, await context.LocationTypes.CountAsync(t => t.CompanyId == companyId));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Does_Not_Duplicate_Existing_Home_Location_Or_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow);
        var type = HR.Modules.Employees.Domain.LocationType.Create(Guid.NewGuid(), companyId, "Remote", null, now);
        var home = HR.Modules.Employees.Domain.Location.Create(Guid.NewGuid(), companyId, type.Id, "Home", null, now);
        context.LocationTypes.Add(type);
        context.Locations.Add(home);
        await context.SaveChangesAsync();

        await BuildSeeder(context).SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(home.Id, (await context.Locations.SingleAsync(l => l.CompanyId == companyId && l.Name == "Home")).Id);
        Assert.Equal(1, await context.LocationTypes.CountAsync(t => t.CompanyId == companyId && t.Name == "Remote"));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Does_Not_Touch_Other_Companies()
    {
        await using var context = BuildContext();
        var otherCompanyId = Guid.NewGuid();

        await BuildSeeder(context).SeedDefaultsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, await context.Locations.CountAsync(l => l.CompanyId == otherCompanyId));
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
