using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Sickness;

namespace HR.Modules.Sickness.Features.FulfilEvidenceRequest;

internal sealed class SicknessEvidenceUploadCompletionAction(
    SicknessDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Sickness;
    public TaskActionType ActionType => TaskActionType.Upload;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated evidence request."));

        var evidenceRequest = await db.SicknessEvidenceRequests
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId.Value
                  && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (evidenceRequest is null)
            return Result.Failure(Error.NotFound("The associated evidence request was not found."));

        // Ticket 15 (P1): "already fulfilled" proves only that the request/record mutation
        // committed — it says nothing about whether the audit event that a prior attempt may have
        // been interrupted before publishing ever went out. Recover it (only for a genuine
        // Tasks-dispatch replay, identified by a stable DispatchOperationId — ticket 11; not for an
        // arbitrary already-resolved call with no dispatch identity, which could be an unrelated,
        // long-settled fulfilment and must not unconditionally republish its audit event).
        if (evidenceRequest.Status == SicknessEvidenceRequestStatus.Fulfilled)
        {
            if (context.DispatchOperationId != Guid.Empty)
                await RecoverFulfilmentAuditAsync(evidenceRequest, context, cancellationToken);

            return Result.Success();
        }

        var now = clock.UtcNowOffset();

        evidenceRequest.Fulfil(now);

        var sicknessRecord = await db.SicknessRecords
            .FirstOrDefaultAsync(
                r => r.Id == evidenceRequest.SicknessRecordId
                  && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (sicknessRecord is not null)
            sicknessRecord.ReceiveEvidence(now);

        await db.SaveChangesAsync(cancellationToken);

        if (sicknessRecord is not null)
        {
            await auditPublisher.PublishAsync(
                new SicknessEvidenceFulfilledAuditEvent(
                    EvidenceRequestId: evidenceRequest.Id,
                    SicknessRecordId:  evidenceRequest.SicknessRecordId,
                    CompanyId:         evidenceRequest.CompanyId,
                    EmployeeId:        sicknessRecord.EmployeeId,
                    ActorId:           context.CompletedBy,
                    FulfilledAt:       now,
                    OccurredAt:        now),
                cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Ticket 15 (P1): re-publishes the fulfilment audit event for a request that is already
    /// Fulfilled but may have been interrupted before its audit event went out. Safe to call
    /// unconditionally — the event's EventId is deterministic (== EvidenceRequestId), so
    /// DbAuditEventPublisher's unique-index dedupe makes a repeat publish attempt a guaranteed
    /// no-op once the original has committed.
    /// </summary>
    private async Task RecoverFulfilmentAuditAsync(
        SicknessEvidenceRequest evidenceRequest, TaskCompletionContext context, CancellationToken cancellationToken)
    {
        var sicknessRecord = await db.SicknessRecords
            .FirstOrDefaultAsync(
                r => r.Id == evidenceRequest.SicknessRecordId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (sicknessRecord is null)
            return;

        var now = clock.UtcNowOffset();

        await auditPublisher.PublishAsync(
            new SicknessEvidenceFulfilledAuditEvent(
                EvidenceRequestId: evidenceRequest.Id,
                SicknessRecordId:  evidenceRequest.SicknessRecordId,
                CompanyId:         evidenceRequest.CompanyId,
                EmployeeId:        sicknessRecord.EmployeeId,
                ActorId:           context.CompletedBy,
                FulfilledAt:       now,
                OccurredAt:        now),
            cancellationToken);
    }
}
