using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnProbationExtended;

/// <summary>
/// PROB-07: completes the "add timeline events for extended and failed outcomes" requirement.
/// Summary carries only the new expected end date (a non-sensitive, structured fact) — never the
/// free-text extension reason recorded on the probation record.
/// </summary>
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
