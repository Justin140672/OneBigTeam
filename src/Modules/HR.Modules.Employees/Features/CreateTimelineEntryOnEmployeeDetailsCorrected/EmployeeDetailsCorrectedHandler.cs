using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeDetailsCorrected;

// Deliberately generic — the source event carries no field-level detail (see
// EmployeeDetailsCorrectedIntegrationEvent's doc comment), so no changed-field information is
// available (or wanted) here.
internal sealed class EmployeeDetailsCorrectedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<EmployeeDetailsCorrectedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeDetailsCorrectedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.EmployeeDetailsCorrected,
                EmployeeTimelineCategory.Employment,
                "Employee details updated",
                "Employee details were updated.",
                performedByUserId: null,
                "Employees",
                sourceRecordId: null,
                EmployeeTimelineVisibility.EmployeeAndHr,
                e.OccurredAt),
            cancellationToken);
    }
}
