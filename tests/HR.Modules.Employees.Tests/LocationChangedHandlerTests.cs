using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnLocationChanged;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class LocationChangedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry_With_Resolved_Location_Names()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var previousLocation = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "London", null, now);
        var newLocation = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Manchester", null, now);
        context.LocationTypes.Add(locationType);
        context.Locations.AddRange(previousLocation, newLocation);
        await context.SaveChangesAsync();

        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new LocationChangedHandler(context, timelineWriter);

        var employeeId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeeLocationChangedIntegrationEvent(companyId, employeeId, previousLocation.Id, newLocation.Id, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.LocationChanged, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Contains("London", entry.Summary);
        Assert.Contains("Manchester", entry.Summary);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Generic_Phrasing_When_Locations_Not_Found()
    {
        await using var context = BuildContext();
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new LocationChangedHandler(context, timelineWriter);

        var occurredAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeeLocationChangedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Contains("their previous location", entry.Summary);
        Assert.Contains("a new location", entry.Summary);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
