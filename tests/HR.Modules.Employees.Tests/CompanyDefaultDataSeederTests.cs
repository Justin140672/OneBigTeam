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
        var seeder = new CompanyDefaultDataSeeder(context, new FakeClock(FixedUtcNow), leavePolicyProvisioner);

        await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, await context.Departments.CountAsync(d => d.CompanyId == companyId));
        Assert.Equal(1, await context.LocationTypes.CountAsync(lt => lt.CompanyId == companyId));
        Assert.Equal(1, await context.Locations.CountAsync(l => l.CompanyId == companyId));
        Assert.Equal(1, await context.EmploymentTypes.CountAsync(et => et.CompanyId == companyId));
        Assert.Equal(1, await context.PositionProfiles.CountAsync(pp => pp.CompanyId == companyId));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Returns_Ids_That_Correspond_To_The_Created_Rows()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var leavePolicyProvisioner = new FakeLeavePolicyProvisioner();
        var seeder = new CompanyDefaultDataSeeder(context, new FakeClock(FixedUtcNow), leavePolicyProvisioner);

        var result = await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.DepartmentId);
        Assert.NotEqual(Guid.Empty, result.LocationId);
        Assert.NotEqual(Guid.Empty, result.PositionProfileId);
        Assert.NotEqual(Guid.Empty, result.EmploymentTypeId);

        var department = await context.Departments.SingleAsync(d => d.CompanyId == companyId);
        Assert.Equal(result.DepartmentId, department.Id);
        Assert.Equal("General", department.Name);

        var location = await context.Locations.SingleAsync(l => l.CompanyId == companyId);
        Assert.Equal(result.LocationId, location.Id);
        Assert.Equal("Head Office", location.Name);

        var locationType = await context.LocationTypes.SingleAsync(lt => lt.CompanyId == companyId);
        Assert.Equal(locationType.Id, location.LocationTypeId);
        Assert.Equal("Office", locationType.Name);

        var employmentType = await context.EmploymentTypes.SingleAsync(et => et.CompanyId == companyId);
        Assert.Equal(result.EmploymentTypeId, employmentType.Id);
        Assert.Equal("Full-time", employmentType.Name);

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
        var seeder = new CompanyDefaultDataSeeder(context, new FakeClock(FixedUtcNow), leavePolicyProvisioner);

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
        var seeder = new CompanyDefaultDataSeeder(context, new FakeClock(FixedUtcNow), leavePolicyProvisioner);

        await seeder.SeedDefaultsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, leavePolicyProvisioner.CallCount);
        Assert.Equal(companyId, Assert.Single(leavePolicyProvisioner.RequestedCompanyIds));
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
