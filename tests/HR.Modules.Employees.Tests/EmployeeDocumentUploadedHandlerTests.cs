using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeDocumentUploaded;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class EmployeeDocumentUploadedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_HrOnly_Timeline_Entry_Including_Document_Type()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new EmployeeDocumentUploadedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new EmployeeDocumentUploadedIntegrationEvent(companyId, employeeId, documentId, "Passport", occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.EmployeeDocumentAdded, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Documents, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.HrOnly, entry.Visibility);
        Assert.Equal(documentId, entry.SourceRecordId);
        Assert.Contains("Passport", entry.Summary);
    }
}
