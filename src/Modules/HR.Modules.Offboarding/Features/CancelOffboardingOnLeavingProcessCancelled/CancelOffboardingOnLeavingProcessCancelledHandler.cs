using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Offboarding.Features.CancelOffboardingOnLeavingProcessCancelled;

internal sealed class CancelOffboardingOnLeavingProcessCancelledHandler(
    IOffboardingPlanCoordinator offboardingPlanCoordinator)
    : IRequiredIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>
{
    public Task HandleAsync(
        EmployeeLeavingProcessCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return offboardingPlanCoordinator.CancelOutstandingTasksAsync(
            integrationEvent.CompanyId, integrationEvent.EmployeeId, cancellationToken);
    }
}
