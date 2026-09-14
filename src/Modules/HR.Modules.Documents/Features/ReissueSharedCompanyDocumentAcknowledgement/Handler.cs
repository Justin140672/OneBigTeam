using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-send reminder tasks/notifications.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ReissueSharedCompanyDocumentAcknowledgementResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ReissueSharedCompanyDocumentAcknowledgementResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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

        var now = clock.UtcNowOffset();

        if (eligibleEmployeeIds.Count == 0)
        {
            var emptyResponse = new ReissueSharedCompanyDocumentAcknowledgementResponse(0);

            if (request.IdempotencyKey is { } emptyKey)
            {
                var emptyOutcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
                    scope, emptyKey, fingerprint!, StatusCodes.Status200OK, emptyResponse, now, cancellationToken);

                if (emptyOutcome.Kind == IdempotencyOutcomeKind.Replayed)
                    return Result.Success(emptyOutcome.Response!);
            }

            return Result.Success(emptyResponse);
        }

        var acknowledgedEmployeeIds = await db.SharedCompanyDocumentAcknowledgements
            .AsNoTracking()
            .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
            .Select(a => a.EmployeeId)
            .ToListAsync(cancellationToken);

        var acknowledged = new HashSet<Guid>(acknowledgedEmployeeIds);
        var outstandingEmployeeIds = eligibleEmployeeIds.Where(id => !acknowledged.Contains(id)).ToList();

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

        var response = new ReissueSharedCompanyDocumentAcknowledgementResponse(notifiedCount);

        if (request.IdempotencyKey is { } key)
        {
            // Nothing else on this DbContext needed saving; this only persists the idempotency
            // record itself (with the final response) so a retry of the same key can be replayed.
            await db.SaveIdempotentAsync(db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);
        }

        return Result.Success(response);
    }
}
