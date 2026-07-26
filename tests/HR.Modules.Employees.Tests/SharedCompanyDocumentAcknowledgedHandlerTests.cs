using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnSharedCompanyDocumentAcknowledged;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class SharedCompanyDocumentAcknowledgedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry_Including_Document_Title()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new SharedCompanyDocumentAcknowledgedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new SharedCompanyDocumentAcknowledgedIntegrationEvent(companyId, employeeId, documentId, "Employee Handbook 2026", occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.CompanyDocumentAcknowledged, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Documents, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.EmployeeAndHr, entry.Visibility);
        Assert.Equal(documentId, entry.SourceRecordId);
        Assert.Contains("Employee Handbook 2026", entry.Summary);
    }
}
