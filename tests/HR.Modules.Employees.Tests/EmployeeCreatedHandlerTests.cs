using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeCreated;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class EmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Writes_EmployeeJoined_Timeline_Entry()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new EmployeeCreatedHandler(timelineWriter, new FakeClock(FixedUtcNow));

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, Guid.NewGuid(), startDate.AddMonths(6)),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(startDate, entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.EmployeeJoined, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.DoesNotContain("imported", entry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Notes_Import_In_Summary_When_Imported()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new EmployeeCreatedHandler(timelineWriter, new FakeClock(FixedUtcNow));

        var startDate = new DateOnly(2026, 7, 1);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), startDate, null, startDate.AddMonths(6), IsImported: true),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Contains("imported", entry.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
