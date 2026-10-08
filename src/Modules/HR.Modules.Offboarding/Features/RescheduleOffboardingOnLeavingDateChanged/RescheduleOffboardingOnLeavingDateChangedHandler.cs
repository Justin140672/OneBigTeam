using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Offboarding.Features.RescheduleOffboardingOnLeavingDateChanged;

internal sealed class RescheduleOffboardingOnLeavingDateChangedHandler(
    IOffboardingPlanCoordinator offboardingPlanCoordinator)
    : IRequiredIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>
{
    public Task HandleAsync(
        EmployeeLeavingDateSetIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return offboardingPlanCoordinator.RescheduleOutstandingTasksAsync(
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            integrationEvent.LastWorkingDay,
            cancellationToken);
    }
}
