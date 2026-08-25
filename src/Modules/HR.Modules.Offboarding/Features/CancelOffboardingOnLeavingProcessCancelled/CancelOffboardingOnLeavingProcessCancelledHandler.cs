using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Offboarding.Features.CancelOffboardingOnLeavingProcessCancelled;

/// <summary>
/// OFF-01: second consumer of <c>EmployeeLeavingProcessCancelledIntegrationEvent</c> (alongside
/// the Leave module's entitlement-restoration consumer). Ensures the employee's offboarding plan
/// and its outstanding tasks — both the local OffboardingTask checklist rows and the
/// corresponding Tasks-module TaskItems — end up cancelled whenever a leaving process is
/// withdrawn, even if the direct, synchronous
/// <see cref="IOffboardingPlanCoordinator.CancelOutstandingTasksAsync"/> call already made by
/// Employees' CancelLeavingProcessHandler failed or never ran for some reason (e.g. an
/// out-of-process retry of a previously-persisted event). IntegrationEventPublisher isolates
/// every handler in its own try/catch, so a failure here can never abort the Leaving Process
/// cancellation request itself, nor the Leave module's own handling of the same event.
///
/// The whole operation is delegated to <see cref="IOffboardingPlanCoordinator.CancelOutstandingTasksAsync"/>
/// itself, which is already idempotent (see its own remarks) — so redelivery of this event, or
/// this handler running after the direct call already completed the work, is always a safe no-op
/// beyond re-confirming the Tasks-module side is in sync.
/// </summary>
internal sealed class CancelOffboardingOnLeavingProcessCancelledHandler(
    IOffboardingPlanCoordinator offboardingPlanCoordinator)
    : IIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>
{
    public Task HandleAsync(
        EmployeeLeavingProcessCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return offboardingPlanCoordinator.CancelOutstandingTasksAsync(
            integrationEvent.CompanyId, integrationEvent.EmployeeId, cancellationToken);
    }
}
