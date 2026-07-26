using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnOnboardingCompleted;

internal sealed class OnboardingCompletedHandler(
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<OnboardingCompletedIntegrationEvent>
{
    public async Task HandleAsync(OnboardingCompletedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.OnboardingCompleted,
                EmployeeTimelineCategory.OnboardingAndProbation,
                "Onboarding completed",
                "Onboarding checklist completed.",
                performedByUserId: null,
                "Onboarding",
                e.OnboardingPlanId,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
