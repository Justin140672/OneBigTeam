using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreatePositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreatePositionProfileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<(Department Department, Location Location)> SeedDepartmentAndLocationAsync(
        EmployeesDbContext context, Guid companyId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "London", null, now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();
        return (department, location);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Software Developer", result.Value.Title);
        Assert.Equal(department.Id, result.Value.DepartmentId);
        Assert.Equal(location.Id, result.Value.LocationId);
        Assert.True(result.Value.IsActive);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile_With_Department()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Engineering Manager"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(department.Id, result.Value!.DepartmentId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Title_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        context.PositionProfiles.Add(
            PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Software Developer", null, null, null, null, null, null, null, Guid.NewGuid(), now));
        await context.SaveChangesAsync();

        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (_, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = Guid.NewGuid(),
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_Department_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var (_, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var otherCompanyId = Guid.NewGuid();
        var department = Department.Create(Guid.NewGuid(), otherCompanyId, "Engineering", null, now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_Location_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, _) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = Guid.NewGuid(),
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Title_In_Different_Companies()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (departmentA, locationA) = await SeedDepartmentAndLocationAsync(context, companyA);
        var (departmentB, locationB) = await SeedDepartmentAndLocationAsync(context, companyB);

        context.PositionProfiles.Add(
            PositionProfile.Create(Guid.NewGuid(), companyA, departmentA.Id, locationA.Id, "Software Developer", null, null, null, null, null, null, null, Guid.NewGuid(), now));
        await context.SaveChangesAsync();

        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyB,
                DepartmentId = departmentB.Id,
                LocationId = locationB.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile_With_Valid_DefaultLeavePolicyId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: true));
        var leavePolicyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                Title = "Software Developer",
                DefaultLeavePolicyId = leavePolicyId
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(leavePolicyId, result.Value!.DefaultLeavePolicyId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_DefaultLeavePolicyId_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: false));

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                Title = "Software Developer",
                DefaultLeavePolicyId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile_With_WorkingPattern_And_SalaryRange()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer",
                WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday,
                HoursPerDayOverride = 8m,
                SalaryMin = 40000,
                SalaryMax = 60000
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday, result.Value!.WorkingDaysOverride);
        Assert.Equal(8m, result.Value.HoursPerDayOverride);
        Assert.Equal(40000, result.Value.SalaryMin);
        Assert.Equal(60000, result.Value.SalaryMax);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile_With_SalaryType()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer",
                SalaryMin = 40000,
                SalaryMax = 60000,
                SalaryType = SalaryType.Annual
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalaryType.Annual, result.Value!.SalaryType);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.Equal(SalaryType.Annual, saved.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile_With_Null_SalaryType()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Creates_PositionProfile_With_Valid_OnboardingTemplateId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer",
                OnboardingTemplateId = template.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(template.Id, result.Value!.OnboardingTemplateId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_OnboardingTemplateId_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var handler = new CreatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new CreatePositionProfileRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Software Developer",
                OnboardingTemplateId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
