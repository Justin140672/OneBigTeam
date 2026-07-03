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

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        var evidenceRequest = await db.SicknessEvidenceRequests
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId.Value
                  && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (evidenceRequest is null)
            return;

        if (evidenceRequest.Status == SicknessEvidenceRequestStatus.Fulfilled)
            return;

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
                    FulfilledAt:       now,
                    OccurredAt:        now),
                cancellationToken);
        }
    }
}
