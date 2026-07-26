using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnSharedCompanyDocumentAcknowledged;

internal sealed class SharedCompanyDocumentAcknowledgedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<SharedCompanyDocumentAcknowledgedIntegrationEvent>
{
    public async Task HandleAsync(SharedCompanyDocumentAcknowledgedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.CompanyDocumentAcknowledged,
                EmployeeTimelineCategory.Documents,
                "Company document acknowledged",
                $"Acknowledged \"{e.DocumentTitle}\".",
                performedByUserId: null,
                "Documents",
                e.DocumentId,
                EmployeeTimelineVisibility.EmployeeAndHr,
                e.OccurredAt),
            cancellationToken);
    }
}
