using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Services;
using HR.SharedKernel;

namespace HR.Modules.Offboarding.Features.ReassignOffboardingTasksOnManagerChanged;

internal sealed class ReassignOffboardingTasksOnManagerChangedHandler(OffboardingManagerTaskAssigneeReconciler reconciler)
    : IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>
{
    public async Task HandleAsync(
        EmployeeManagerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await reconciler.ReconcileEmployeeAsync(
            integrationEvent.CompanyId, integrationEvent.EmployeeId, cancellationToken);
    }
}
