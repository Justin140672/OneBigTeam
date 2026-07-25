using HR.Infrastructure.Abstractions;
using HR.Modules.Employees;
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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
        var auditPublisher = new FakeAuditPublisher();
        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), auditPublisher);

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
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var auditPublisher = new FakeAuditPublisher();
        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), auditPublisher);

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
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: false), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: true), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
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

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

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
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_NoticePeriodOverride()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                NoticePeriodUnitOverride = NoticePeriodUnit.Months,
                NoticePeriodLengthOverride = 2
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoticePeriodUnit.Months, result.Value!.NoticePeriodUnitOverride);
        Assert.Equal(2, result.Value.NoticePeriodLengthOverride);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.Equal(NoticePeriodUnit.Months, saved.NoticePeriodUnitOverride);
        Assert.Equal(2, saved.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task HandleAsync_Allows_Clearing_NoticePeriodOverride_To_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);

        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now,
            noticePeriodUnitOverride: NoticePeriodUnit.Weeks, noticePeriodLengthOverride: 4);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = Guid.NewGuid(),
                NoticePeriodUnitOverride = null,
                NoticePeriodLengthOverride = null
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.NoticePeriodUnitOverride);
        Assert.Null(result.Value.NoticePeriodLengthOverride);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.Null(saved.NoticePeriodUnitOverride);
        Assert.Null(saved.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task HandleAsync_Publishes_PositionProfileUpdatedAuditEvent_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (department, location) = await SeedDepartmentAndLocationAsync(context, companyId);
        var originalLeavePolicyId = Guid.NewGuid();

        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Old Title", null, null, null, null, null, null, null, originalLeavePolicyId, now,
            noticePeriodUnitOverride: NoticePeriodUnit.Weeks, noticePeriodLengthOverride: 4);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(), auditPublisher);
        var actorEmployeeId = Guid.NewGuid();
        var newLeavePolicyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                DepartmentId = department.Id,
                LocationId = location.Id,
                DefaultLeavePolicyId = newLeavePolicyId,
                Title = "New Title",
                Description = "Updated description",
                NoticePeriodUnitOverride = NoticePeriodUnit.Months,
                NoticePeriodLengthOverride = 2
            },
            actorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var auditEvent = Assert.IsType<PositionProfileUpdatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(profile.Id, auditEvent.PositionProfileId);
        Assert.Equal(actorEmployeeId, auditEvent.ActorEmployeeId);

        Assert.Equal("Old Title", auditEvent.Before.Title);
        Assert.Equal(originalLeavePolicyId, auditEvent.Before.DefaultLeavePolicyId);
        Assert.Equal(NoticePeriodUnit.Weeks, auditEvent.Before.NoticePeriodUnitOverride);
        Assert.Equal(4, auditEvent.Before.NoticePeriodLengthOverride);

        Assert.Equal("New Title", auditEvent.After.Title);
        Assert.Equal("Updated description", auditEvent.After.Description);
        Assert.Equal(newLeavePolicyId, auditEvent.After.DefaultLeavePolicyId);
        Assert.Equal(NoticePeriodUnit.Months, auditEvent.After.NoticePeriodUnitOverride);
        Assert.Equal(2, auditEvent.After.NoticePeriodLengthOverride);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
