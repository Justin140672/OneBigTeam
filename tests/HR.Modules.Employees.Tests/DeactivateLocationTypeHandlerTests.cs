using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeactivateLocationType;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeactivateLocationTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_LocationType()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var entity = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        context.LocationTypes.Add(entity);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLocationTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.LocationTypes.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeactivateLocationTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLocationTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var entity = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        entity.Deactivate(FixedOffset);
        context.LocationTypes.Add(entity);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLocationTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_LocationType_Has_Active_Location()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var entity = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        context.LocationTypes.Add(entity);

        var location = Location.Create(Guid.NewGuid(), companyId, entity.Id, "Head Office", null, FixedOffset);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLocationTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("Office", result.Error.Message);
        Assert.Contains("1 active location", result.Error.Message);

        var saved = await context.LocationTypes.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_LocationType_When_Only_Inactive_Locations_Use_It()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var entity = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, FixedOffset);
        context.LocationTypes.Add(entity);

        var location = Location.Create(Guid.NewGuid(), companyId, entity.Id, "Head Office", null, FixedOffset);
        location.Deactivate(FixedOffset);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new DeactivateLocationTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateLocationTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.LocationTypes.SingleAsync();
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
