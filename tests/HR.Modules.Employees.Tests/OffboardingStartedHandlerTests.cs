using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnOffboardingStarted;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class OffboardingStartedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new OffboardingStartedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new OffboardingStartedIntegrationEvent(companyId, employeeId, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.OffboardingStarted, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Offboarding, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Equal("Offboarding", entry.SourceModule);
    }
}
