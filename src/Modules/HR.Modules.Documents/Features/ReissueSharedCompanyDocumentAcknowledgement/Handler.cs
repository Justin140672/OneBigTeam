using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ReissueSharedCompanyDocumentAcknowledgement;

internal sealed class ReissueSharedCompanyDocumentAcknowledgementHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    INotificationWriter notificationWriter,
    ITaskCreator taskCreator,
    IOpenTaskBySourceEntityReader openTaskReader,
    IClock clock)
{
    public async Task<Result<ReissueSharedCompanyDocumentAcknowledgementResponse>> HandleAsync(
        ReissueSharedCompanyDocumentAcknowledgementRequest request,
        Guid reissuedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<ReissueSharedCompanyDocumentAcknowledgementResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status != SharedCompanyDocumentStatus.Published)
            return Result.Failure<ReissueSharedCompanyDocumentAcknowledgementResponse>(
                Error.Validation("Only a published document can have its acknowledgement reissued."));

        if (!document.RequiresAcknowledgement)
            return Result.Failure<ReissueSharedCompanyDocumentAcknowledgementResponse>(
                Error.Validation("This document does not require acknowledgement."));

        var eligibleEmployeeIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(
            document.CompanyId, document.Id, cancellationToken);

        if (eligibleEmployeeIds.Count == 0)
            return Result.Success(new ReissueSharedCompanyDocumentAcknowledgementResponse(0));

        var acknowledgedEmployeeIds = await db.SharedCompanyDocumentAcknowledgements
            .AsNoTracking()
            .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
            .Select(a => a.EmployeeId)
            .ToListAsync(cancellationToken);

        var acknowledged = new HashSet<Guid>(acknowledgedEmployeeIds);
        var outstandingEmployeeIds = eligibleEmployeeIds.Where(id => !acknowledged.Contains(id)).ToList();

        var now = clock.UtcNowOffset();
        var notifiedCount = 0;

        foreach (var employeeId in outstandingEmployeeIds)
        {
            // Mirrors SharedCompanyDocumentAcknowledgementReminderJob's reconciliation path: an
            // employee with no open Acknowledge task for this document gets one created here
            // (notifyAssignee: false because the explicit reminder notification below covers it).
            var existingTaskId = await openTaskReader.GetOpenTaskIdForAssigneeAsync(
                document.CompanyId, document.Id, employeeId, TaskActionType.Acknowledge, cancellationToken);

            Guid taskId;

            if (existingTaskId is null)
            {
                taskId = await taskCreator.CreateAsync(
                    document.CompanyId,
                    createdBy:          reissuedBy,
                    title:              $"Acknowledge: {document.Title} (v{document.VersionNumber})",
                    description:        $"Please read and acknowledge '{document.Title}'.",
                    priority:           TaskPriority.Medium,
                    source:             TaskSource.Document,
                    actionType:         TaskActionType.Acknowledge,
                    dueDate:            document.AcknowledgementDueDate,
                    assignedEmployeeId: employeeId,
                    assignedUserId:     employeeId,
                    sourceEntityId:     document.Id,
                    cancellationToken,
                    notifyAssignee:     false);
            }
            else
            {
                taskId = existingTaskId.Value;
            }

            // Unlike the daily reminder job's interval-gated SendIfIntervalElapsedAsync, this is an
            // explicit "nudge now" HR action: every outstanding employee always gets a fresh
            // notification here, regardless of when they were last reminded.
            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                document.CompanyId,
                employeeId,
                "Reminder: document acknowledgement required",
                $"Please read and acknowledge '{document.Title}'.",
                taskId,
                NotificationType.SharedCompanyDocumentAcknowledgementReminder,
                NotificationPriority.Normal,
                now,
                cancellationToken);

            notifiedCount++;
        }

        return Result.Success(new ReissueSharedCompanyDocumentAcknowledgementResponse(notifiedCount));
    }
}
