using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnProbationExtended;

internal sealed class ProbationExtendedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<ProbationExtendedIntegrationEvent>
{
    public async Task HandleAsync(ProbationExtendedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.ProbationExtended,
                EmployeeTimelineCategory.OnboardingAndProbation,
                "Probation extended",
                $"Probation period extended to {e.NewExpectedEndDate:d MMM yyyy}.",
                performedByUserId: null,
                "Probation",
                e.ProbationRecordId,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
