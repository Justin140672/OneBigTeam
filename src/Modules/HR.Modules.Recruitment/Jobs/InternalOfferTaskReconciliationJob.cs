using Hangfire;
using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Jobs;

internal sealed class InternalOfferTaskReconciliationJob(
    InternalOfferTaskEffectsService effectsService,
    InternalOfferSnapshotBackfillService backfillService)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        await backfillService.BackfillAsync(CancellationToken.None);
        await effectsService.RunAllOutstandingAsync(CancellationToken.None);
    }
}
