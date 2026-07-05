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

    [Fact]
    public async Task HandleAsync_Updates_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Old Title", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "New Title",
                Description = "Updated description",
                IsManagerial = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.True(result.Value.IsManagerial);

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

        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Engineer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = profile.Id,
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

        var profile1 = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, now);
        var profile2 = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Manager", null, true, null, null, null, null, null, null, now);
        context.PositionProfiles.AddRange(profile1, profile2);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        // Try to rename profile1 to the same title as profile2
        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile1.Id,
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

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                IsManagerial = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsManagerial);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = Guid.NewGuid()
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

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        context.Departments.Add(department);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader());

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
                DepartmentId = department.Id
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

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: false));

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
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

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new UpdatePositionProfileHandler(context, new FakeClock(FixedUtcNow), new FakeLeavePolicyReader(exists: true));

        var result = await handler.HandleAsync(
            new UpdatePositionProfileRequest
            {
                CompanyId = companyId,
                Id = profile.Id,
                Title = "Engineer",
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

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
