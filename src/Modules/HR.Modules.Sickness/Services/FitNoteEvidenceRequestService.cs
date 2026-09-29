using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

internal sealed class FitNoteEvidenceRequestService(
    SicknessDbContext db,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher,
    ITaskRescheduler taskRescheduler)
{
    internal static readonly Guid SystemActorId = Guid.Empty;
    private const int DueDateDaysFromNow = 7;

    public async Task<bool> RequestIfEligibleAsync(
        SicknessRecord record,
        int fitNoteRequiredAfterDays,
        DateOnly evaluationDate,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool evaluationDateIsFinal = false)
    {
        if (record.EvidenceStatus == SicknessEvidenceStatus.Received ||
            record.EvidenceStatus == SicknessEvidenceStatus.Waived)
            return false;

        if (!FitNoteEvaluator.IsThresholdReached(record.StartDate, evaluationDate, fitNoteRequiredAfterDays))
            return false;

        var existingRequest = await db.SicknessEvidenceRequests
            .SingleOrDefaultAsync(
                e => e.SicknessRecordId == record.Id && e.Status != SicknessEvidenceRequestStatus.Cancelled,
                cancellationToken);

        if (existingRequest is not null)
        {
            if (evaluationDateIsFinal)
            {
                var correctedDueDate = evaluationDate.AddDays(DueDateDaysFromNow);
                if (existingRequest.Reschedule(correctedDueDate, now))
                {
                    await db.SaveChangesAsync(cancellationToken);
                    await taskRescheduler.RescheduleManyBySourceEntitiesAsync(
                        record.CompanyId,
                        [existingRequest.Id],
                        TaskSource.Sickness,
                        TaskActionType.Upload,
                        correctedDueDate,
                        cancellationToken);
                }
            }

            return false;
        }

        var dueDate = evaluationDate.AddDays(DueDateDaysFromNow);

        var request = SicknessEvidenceRequest.Create(
            Guid.NewGuid(),
            record.CompanyId,
            record.Id,
            SystemActorId,
            dueDate,
            null,
            now);

        db.SicknessEvidenceRequests.Add(request);

        if (record.EvidenceStatus != SicknessEvidenceStatus.Pending)
            record.MarkEvidencePending(now);

        await db.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(
            new SicknessEvidenceRequestedIntegrationEvent(
                CompanyId:         record.CompanyId,
                EmployeeId:        record.EmployeeId,
                SicknessRecordId:  record.Id,
                EvidenceRequestId: request.Id,
                DueDate:           dueDate,
                OccurredAt:        now),
            cancellationToken);

        await auditPublisher.PublishAsync(
            new SicknessEvidenceRequestedAuditEvent(
                EvidenceRequestId: request.Id,
                SicknessRecordId:  record.Id,
                CompanyId:         record.CompanyId,
                EmployeeId:        record.EmployeeId,
                ActorId:           SystemActorId,
                DueDate:           dueDate,
                OccurredAt:        now),
            cancellationToken);

        return true;
    }
}
