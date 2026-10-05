using Hangfire;
using HR.Modules.Tasks.Services;

namespace HR.Modules.Tasks.Jobs;

internal sealed class TaskRecoveryAuditDeliveryJob(TaskRecoveryAuditDelivery delivery)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync() => await delivery.DeliverOutstandingAsync(CancellationToken.None);
}
