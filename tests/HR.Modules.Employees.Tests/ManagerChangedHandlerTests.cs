using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnManagerChanged;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ManagerChangedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry_With_Resolved_Manager_Names()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var previousManager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Old", "jane@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var newManager = Employee.Create(Guid.NewGuid(), companyId, "Bob", "New", "bob@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(previousManager, newManager);
        await context.SaveChangesAsync();

        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new ManagerChangedHandler(context, timelineWriter);

        var employeeId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, previousManager.Id, newManager.Id, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.ManagerChanged, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Contains("Jane Old", entry.Summary);
        Assert.Contains("Bob New", entry.Summary);
    }

    [Fact]
    public async Task HandleAsync_Uses_No_Manager_Phrasing_When_ManagerId_Is_Null()
    {
        await using var context = BuildContext();
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new ManagerChangedHandler(context, timelineWriter);

        var occurredAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), null, null, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Contains("no manager", entry.Summary);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
