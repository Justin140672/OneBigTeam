using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnProbationFailed;

/// <summary>
/// PROB-07: completes the "add timeline events for extended and failed outcomes" requirement —
/// previously only a Pass outcome produced a timeline entry (see ProbationPassedHandler). Summary
/// text is deliberately generic ("Probation period failed.") and never includes the free-text
/// outcome notes recorded on the probation review/record.
/// </summary>
internal sealed class ProbationFailedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<ProbationFailedIntegrationEvent>
{
    public async Task HandleAsync(ProbationFailedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.ProbationFailed,
                EmployeeTimelineCategory.OnboardingAndProbation,
                "Probation failed",
                "Probation period failed.",
                performedByUserId: null,
                "Probation",
                e.ProbationRecordId,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
