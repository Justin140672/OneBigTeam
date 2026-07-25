using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeactivatePositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeactivatePositionProfileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_Active_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Software Developer", null,
            probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), FixedOffset);
        context.PositionProfiles.Add(positionProfile);
        await context.SaveChangesAsync();

        var handler = new DeactivatePositionProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivatePositionProfileRequest { CompanyId = companyId, Id = positionProfile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeactivatePositionProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivatePositionProfileRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Already_Inactive_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Software Developer", null,
            probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), FixedOffset);
        positionProfile.Deactivate(FixedOffset);
        context.PositionProfiles.Add(positionProfile);
        await context.SaveChangesAsync();

        var handler = new DeactivatePositionProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivatePositionProfileRequest { CompanyId = companyId, Id = positionProfile.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Wrong_Company()
    {
        await using var context = BuildContext();
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Software Developer", null,
            probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), FixedOffset);
        context.PositionProfiles.Add(positionProfile);
        await context.SaveChangesAsync();

        var handler = new DeactivatePositionProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivatePositionProfileRequest { CompanyId = Guid.NewGuid(), Id = positionProfile.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_PositionProfile_Has_Active_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Software Developer", null,
            probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), FixedOffset);
        context.PositionProfiles.Add(positionProfile);

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", DateOnly.FromDateTime(FixedUtcNow),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), positionProfile.Id, FixedOffset);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new DeactivatePositionProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivatePositionProfileRequest { CompanyId = companyId, Id = positionProfile.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("Software Developer", result.Error.Message);
        Assert.Contains("1 active employee", result.Error.Message);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_PositionProfile_When_Only_Terminated_Employees_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Software Developer", null,
            probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), FixedOffset);
        context.PositionProfiles.Add(positionProfile);

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", DateOnly.FromDateTime(FixedUtcNow),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), positionProfile.Id, FixedOffset);
        employee.SetStatusForTesting(EmploymentStatus.FormerEmployee, FixedOffset);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new DeactivatePositionProfileHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivatePositionProfileRequest { CompanyId = companyId, Id = positionProfile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.PositionProfiles.SingleAsync();
        Assert.False(saved.IsActive);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
