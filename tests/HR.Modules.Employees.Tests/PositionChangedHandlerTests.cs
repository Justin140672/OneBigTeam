using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnPositionChanged;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class PositionChangedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry_With_Resolved_Position_Titles()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "HQ", null, now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);

        var previousPosition = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Analyst", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        var newPosition = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Senior Analyst", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.AddRange(previousPosition, newPosition);
        await context.SaveChangesAsync();

        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new PositionChangedHandler(context, timelineWriter);

        var employeeId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPosition.Id, newPosition.Id, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.PositionChanged, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Contains("Analyst", entry.Summary);
        Assert.Contains("Senior Analyst", entry.Summary);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Generic_Phrasing_When_Position_Profiles_Not_Found()
    {
        await using var context = BuildContext();
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new PositionChangedHandler(context, timelineWriter);

        var occurredAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Contains("their previous role", entry.Summary);
        Assert.Contains("a new role", entry.Summary);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
