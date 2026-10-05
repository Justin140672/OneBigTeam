using Hangfire;
using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Jobs;

internal sealed class InterviewTaskCleanupReconciliationJob(
    InterviewTaskCleanupService cleanupService,
    InterviewTaskEffectsService effectsService,
    InterviewOutcomeTaskReconciliationService outcomeService,
    InterviewOutcomeRepairService repairService,
    InterviewOutcomeRepairAuditDelivery repairAuditDelivery)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        await repairAuditDelivery.DeliverOutstandingAsync(CancellationToken.None);
        await repairService.UnblockResetTasksOperationsAsync(CancellationToken.None);
        await effectsService.RunAllOutstandingAsync(CancellationToken.None);
        await outcomeService.RunAllOutstandingAsync(CancellationToken.None);
        await cleanupService.RunAllOutstandingAsync(CancellationToken.None);
    }
}
