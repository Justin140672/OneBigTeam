using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeactivateLocation;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeactivateLocationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_Active_Location()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, FixedOffset);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateLocationRequest { CompanyId = companyId, Id = location.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.Locations.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Location_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeactivateLocationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateLocationRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Already_Inactive_Location()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, FixedOffset);
        location.Deactivate(FixedOffset);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateLocationRequest { CompanyId = companyId, Id = location.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Location_Has_Active_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, FixedOffset);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", DateOnly.FromDateTime(FixedUtcNow),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), location.Id, Guid.NewGuid(), FixedOffset);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateLocationRequest { CompanyId = companyId, Id = location.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("Head Office", result.Error.Message);
        Assert.Contains("1 active employee", result.Error.Message);

        var saved = await context.Locations.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Location_When_Only_Terminated_Employees_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, FixedOffset);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", DateOnly.FromDateTime(FixedUtcNow),
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), location.Id, Guid.NewGuid(), FixedOffset);
        employee.SetStatusForTesting(EmploymentStatus.FormerEmployee, FixedOffset);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateLocationRequest { CompanyId = companyId, Id = location.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.Locations.SingleAsync();
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
