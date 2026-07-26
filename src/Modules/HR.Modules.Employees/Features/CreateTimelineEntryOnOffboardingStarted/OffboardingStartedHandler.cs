using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnOffboardingStarted;

internal sealed class OffboardingStartedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<OffboardingStartedIntegrationEvent>
{
    public async Task HandleAsync(OffboardingStartedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.OffboardingStarted,
                EmployeeTimelineCategory.Offboarding,
                "Offboarding started",
                "Offboarding process started.",
                performedByUserId: null,
                "Offboarding",
                sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
