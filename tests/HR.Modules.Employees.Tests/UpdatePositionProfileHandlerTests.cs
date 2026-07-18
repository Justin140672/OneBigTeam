using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdatePositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdatePositionProfileHandlerTests
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
    public async Task HandleAsync_Updates_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Old Title", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "New Title",
                Description = "Updated description"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
        Assert.Equal("Updated description", result.Value.Description);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.Equal("New Title", saved.Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = Guid.NewGuid(),
                DepartmentId = Guid.NewGuid(),
                LocationId = Guid.NewGuid(),
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Some Title"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var profileCompanyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, profileCompanyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), profileCompanyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = profile.Id,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "New Title"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Title_Already_Exists_In_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile1 = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        var profile2 = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Manager", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.AddRange(profile1, profile2);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        // Try to rename profile1 to the same title as profile2
        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile1.Id,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Manager"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Updating_With_Same_Title()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                Title = "Engineer"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = Guid.NewGuid(),
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Location_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = Guid.NewGuid(),
                DefaultLeavePolicyId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_With_Valid_Department()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (originalDepartment, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var department = Department.Create(Guid.NewGuid(), companyId, "New Department", null, now);
        context.Departments.Add(department);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, originalDepartment.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(department.Id, result.Value!.DepartmentId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DefaultLeavePolicyId_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: false));

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_WorkingPattern_SalaryRange_And_DefaultLeavePolicy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavePolicyId = Guid.NewGuid();
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: true));

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                WorkingDaysOverride = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
                HoursPerDayOverride = 6m,
                SalaryMin = 45000,
                SalaryMax = 55000,
                DefaultLeavePolicyId = leavePolicyId
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, result.Value!.WorkingDaysOverride);
        Assert.Equal(6m, result.Value.HoursPerDayOverride);
        Assert.Equal(45000, result.Value.SalaryMin);
        Assert.Equal(55000, result.Value.SalaryMax);
        Assert.Equal(leavePolicyId, result.Value.DefaultLeavePolicyId);
    }

    [Fact]
    public async Task HandleAsync_Updates_SalaryType()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                SalaryMin = 45000,
                SalaryMax = 55000,
                SalaryType = SalaryType.Hourly
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalaryType.Hourly, result.Value!.SalaryType);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.Equal(SalaryType.Hourly, saved.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Allows_Clearing_SalaryType_To_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, SalaryType.Annual, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                SalaryType = null
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Updates_With_Valid_OnboardingTemplateId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, now);
        context.OnboardingTemplates.Add(template);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                OnboardingTemplateId = template.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(template.Id, result.Value!.OnboardingTemplateId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_OnboardingTemplateId_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
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
