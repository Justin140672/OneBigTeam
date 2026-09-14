using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.PublishSharedCompanyDocument;

internal sealed class PublishSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    ITaskCreator taskCreator,
    INotificationWriter notificationWriter,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<PublishSharedCompanyDocumentResponse>> HandleAsync(
        PublishSharedCompanyDocumentRequest request,
        Guid publishedBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-fan-out acknowledgement tasks.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, PublishSharedCompanyDocumentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<PublishSharedCompanyDocumentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status != SharedCompanyDocumentStatus.Draft)
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.Conflict("Only draft documents can be published."));

        // A file has been uploaded — structurally guaranteed by Upload/ReplaceFile, checked here
        // defensively rather than trusted blindly.
        if (string.IsNullOrWhiteSpace(document.CurrentFileReference) || string.IsNullOrWhiteSpace(document.FileName))
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.Validation("This document has no uploaded file and cannot be published."));

        // Required metadata complete — Title is guaranteed non-empty by Create/UpdateDetails, but
        // the category could have been deactivated (or, in theory, removed) since upload, so that
        // still needs a fresh check at publish time.
        if (string.IsNullOrWhiteSpace(document.Title))
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.Validation("This document has no title and cannot be published."));

        var categoryIsUsable = await db.CompanyDocumentCategories
            .AnyAsync(c => c.Id == document.CategoryId && c.CompanyId == request.CompanyId && c.IsActive, cancellationToken);

        if (!categoryIsUsable)
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.Validation("This document's category is no longer active and must be changed before publishing."));

        // At least one audience selected — deliberately not checked: no audience rules means
        // "All Employees", a first-class, intentional audience choice in this system (see
        // SharedCompanyDocumentAudienceRule), not an unset/incomplete state.

        // Effective date is valid — the one date relationship that can actually be wrong.
        if (document.EffectiveDate is not null && document.ReviewDate is not null &&
            document.ReviewDate < document.EffectiveDate)
        {
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.Validation("The review date cannot be before the effective date."));
        }

        // Acknowledgement settings complete — a due date is the one setting that genuinely can be
        // missing; the statement is explicitly optional (falls back to a default sentence at
        // display time) and never blocks publishing.
        if (document.RequiresAcknowledgement && document.AcknowledgementDueDate is null)
        {
            return Result.Failure<PublishSharedCompanyDocumentResponse>(
                Error.Validation("An acknowledgement due date is required before this document can be published."));
        }

        var now = clock.UtcNowOffset();
        document.Publish(publishedBy, now);

        // Cross-module task/notification fan-out happens after the save below, so the final task
        // count can't be known before it — response therefore can't be built purely from
        // in-memory values ahead of the save for this handler. The status check above (Draft-only)
        // already prevents this document from being published twice regardless of the key.
        await db.SaveChangesAsync(cancellationToken);

        var acknowledgementTasksCreated = 0;
        if (document.RequiresAcknowledgement)
        {
            var eligibleEmployeeIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(
                request.CompanyId, document.Id, cancellationToken);

            // Duplicate-task prevention: Publish only ever runs on a Draft document (the status
            // check above rejects anything else, and there is no Republish/RevertToDraft
            // endpoint), so a given (document, version) can only trigger this loop once — there
            // is structurally no way to reach this code twice for the same version. The one
            // remaining case worth guarding is an employee who already acknowledged this exact
            // version (e.g. between an earlier publish and a since-fixed metadata edit); they
            // don't need a reminder task.
            var alreadyAcknowledgedIds = await db.SharedCompanyDocumentAcknowledgements
                .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
                .Select(a => a.EmployeeId)
                .ToListAsync(cancellationToken);

            var alreadyAcknowledged = new HashSet<Guid>(alreadyAcknowledgedIds);

            foreach (var employeeId in eligibleEmployeeIds)
            {
                if (alreadyAcknowledged.Contains(employeeId))
                    continue;

                await taskCreator.CreateAsync(
                    request.CompanyId,
                    createdBy:          publishedBy,
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

                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    document.CompanyId,
                    employeeId,
                    "Acknowledgement required",
                    $"Please read and acknowledge '{document.Title}' (version {document.VersionNumber}).",
                    document.Id,
                    NotificationType.SharedCompanyDocumentAcknowledgementReminder,
                    NotificationPriority.Normal,
                    now,
                    cancellationToken);

                acknowledgementTasksCreated++;
            }
        }

        await auditPublisher.PublishAsync(new SharedCompanyDocumentPublishedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            document.VersionNumber,
            document.RequiresAcknowledgement,
            acknowledgementTasksCreated,
            publishedBy,
            now), cancellationToken);

        var response = new PublishSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Title,
            document.Status.ToString(),
            document.PublishedBy!.Value,
            document.PublishedAt!.Value,
            acknowledgementTasksCreated);

        if (request.IdempotencyKey is { } key)
        {
            // The business save/fan-out already happened above; this only persists the idempotency
            // record itself (with the final response) so a retry of the same key can be replayed.
            await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);
        }

        return Result.Success(response);
    }
}
