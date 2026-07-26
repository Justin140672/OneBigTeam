using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeDetailsCorrected;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class EmployeeDetailsCorrectedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_Generic_Timeline_Entry_With_No_Field_Level_Detail()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new EmployeeDetailsCorrectedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(companyId, employeeId, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.EmployeeDetailsCorrected, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.EmployeeAndHr, entry.Visibility);
        Assert.Equal("Employee details updated", entry.Title);
        Assert.Equal("Employee details were updated.", entry.Summary);
    }
}
