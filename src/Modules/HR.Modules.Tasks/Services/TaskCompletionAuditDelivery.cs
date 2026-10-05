using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Services;

/// <summary>
/// Single verified delivery path for the deterministic <see cref="TaskCompletedAuditEvent"/>
/// (EventId = task id), shared by programmatic and interactive completion. The global publisher logs
/// and swallows persistence failures, so a normal return proves nothing: the event is published only
/// when absent and the caller may record a checkpoint only after existence is confirmed.
/// </summary>
internal sealed class TaskCompletionAuditDelivery(
    IAuditEventPublisher auditPublisher,
    IAuditEventExistenceReader auditExistenceReader)
{
    /// <summary>Throws when the event is still absent after publishing (an unconfirmed effect).</summary>
    public async Task EnsureDeliveredAsync(TaskCompletedAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (await auditExistenceReader.ExistsAsync(auditEvent.TaskId, cancellationToken))
            return;

        await auditPublisher.PublishAsync(auditEvent, cancellationToken);

        if (!await auditExistenceReader.ExistsAsync(auditEvent.TaskId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"The task-completed audit event for task {auditEvent.TaskId} was not confirmed as persisted.");
        }
    }

    public Task<bool> ExistsAsync(Guid taskId, CancellationToken cancellationToken) =>
        auditExistenceReader.ExistsAsync(taskId, cancellationToken);
}
