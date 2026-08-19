using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnCompensationChanged;

// No salary/amount figure is ever read from CompensationChangedIntegrationEvent (it deliberately
// doesn't carry one) or written into the Summary below — see EmployeeTimelineVisibility's
// redaction rule.
internal sealed class CompensationChangedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<CompensationChangedIntegrationEvent>
{
    public async Task HandleAsync(CompensationChangedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                e.EffectiveFrom,
                EmployeeTimelineEventType.CompensationChanged,
                EmployeeTimelineCategory.Compensation,
                "Compensation changed",
                "A compensation change was recorded.",
                performedByUserId: null,
                "Employees",
                e.CompensationId,
                EmployeeTimelineVisibility.HrOnly,
                e.OccurredAt),
            cancellationToken);
    }
}
