using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeDocumentUploaded;

internal sealed class EmployeeDocumentUploadedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<EmployeeDocumentUploadedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeDocumentUploadedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.EmployeeDocumentAdded,
                EmployeeTimelineCategory.Documents,
                "Document added",
                $"A {e.DocumentTypeName} document was added.",
                performedByUserId: null,
                "Documents",
                e.EmployeeDocumentId,
                EmployeeTimelineVisibility.HrOnly,
                e.OccurredAt),
            cancellationToken);
    }
}
