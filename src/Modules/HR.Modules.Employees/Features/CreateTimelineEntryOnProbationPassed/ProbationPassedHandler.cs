using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnProbationPassed;

internal sealed class ProbationPassedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<ProbationPassedIntegrationEvent>
{
    public async Task HandleAsync(ProbationPassedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.ProbationPassed,
                EmployeeTimelineCategory.OnboardingAndProbation,
                "Probation passed",
                "Probation period passed.",
                performedByUserId: null,
                "Probation",
                e.ProbationRecordId,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
