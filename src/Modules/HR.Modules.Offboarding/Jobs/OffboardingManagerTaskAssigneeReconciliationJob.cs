using HR.Modules.Offboarding.Services;

namespace HR.Modules.Offboarding.Jobs;

internal sealed class OffboardingManagerTaskAssigneeReconciliationJob(
    OffboardingManagerTaskAssigneeReconciler reconciler)
{
    public Task ExecuteAsync() => reconciler.ReconcileAllActivePlansAsync(CancellationToken.None);
}
