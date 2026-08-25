using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Offboarding.Features.RescheduleOffboardingOnLeavingDateChanged;

/// <summary>
/// OFF-02: consumer of <c>EmployeeLeavingDateSetIntegrationEvent</c> (alongside the Leave module's
/// entitlement-recalculation consumer). Published by Employees' StartLeavingProcess and
/// AmendLeavingProcess handlers whenever the leaving date/last working day is set or amended — this
/// keeps the employee's active offboarding plan and outstanding task due dates aligned with the
/// current LastWorkingDay whenever HR changes it.
///
/// Firing on both "set" (offboarding plan does not exist yet — the whole call is a no-op inside
/// <see cref="IOffboardingPlanCoordinator.RescheduleOutstandingTasksAsync"/>) and "amend" (plan may
/// already exist, may not) is deliberate and requires no branching here: the coordinator method is
/// itself idempotent and safe to call unconditionally for both cases (see its own remarks).
///
/// IntegrationEventPublisher isolates every handler in its own try/catch, so a failure here can
/// never abort the leaving process amendment request itself, nor the Leave module's own handling of
/// the same event.
/// </summary>
internal sealed class RescheduleOffboardingOnLeavingDateChangedHandler(
    IOffboardingPlanCoordinator offboardingPlanCoordinator)
    : IIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>
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
