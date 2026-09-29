using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class UpdateSharedCompanyDocumentAcknowledgementSettingsHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    ITaskCanceller taskCanceller,
    IOpenTaskBySourceEntityReader openTaskReader,
    INotificationWriter notificationWriter,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>> HandleAsync(
        UpdateSharedCompanyDocumentAcknowledgementSettingsRequest request,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var before = new
        {
            document.RequiresAcknowledgement,
            document.AcknowledgementDueDate,
            document.AcknowledgementStatement,
        };

        var normalizedStatement = request.RequiresAcknowledgement && !string.IsNullOrWhiteSpace(request.AcknowledgementStatement)
            ? request.AcknowledgementStatement.Trim()
            : null;
        var normalizedDueDate = request.RequiresAcknowledgement ? request.AcknowledgementDueDate : null;

        if (document.Status != SharedCompanyDocumentStatus.Draft &&
            request.RequiresAcknowledgement &&
            document.AcknowledgementStatement != normalizedStatement)
        {
            return Result.Failure<UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>(
                Error.Conflict("The acknowledgement statement cannot be changed once a document has been published. Upload a new version to change the wording."));
        }

        var hasChanges =
            document.RequiresAcknowledgement != request.RequiresAcknowledgement ||
            document.AcknowledgementDueDate != normalizedDueDate ||
            document.AcknowledgementStatement != normalizedStatement;

        var now = clock.UtcNowOffset();

        var isWithdrawal = document.RequiresAcknowledgement && !request.RequiresAcknowledgement;
        var tasksCancelledCount = 0;
        var notificationsRemovedCount = 0;

        if (isWithdrawal)
        {
            var eligibleEmployeeIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(
                document.CompanyId, document.Id, cancellationToken);

            var acknowledgedEmployeeIds = await db.SharedCompanyDocumentAcknowledgements
                .AsNoTracking()
                .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
                .Select(a => a.EmployeeId)
                .ToListAsync(cancellationToken);

            var acknowledged = new HashSet<Guid>(acknowledgedEmployeeIds);
            var outstandingEmployeeIds = eligibleEmployeeIds.Where(id => !acknowledged.Contains(id));

            foreach (var employeeId in outstandingEmployeeIds)
            {
                var taskId = await openTaskReader.GetOpenTaskIdForAssigneeAsync(
                    document.CompanyId, document.Id, employeeId, TaskActionType.Acknowledge, cancellationToken);

                if (taskId is null)
                    continue;

                notificationsRemovedCount += await notificationWriter.RemoveBySourceEntityAsync(
                    document.CompanyId, taskId.Value, NotificationType.SharedCompanyDocumentAcknowledgementReminder, cancellationToken);
                notificationsRemovedCount += await notificationWriter.RemoveBySourceEntityAsync(
                    document.CompanyId, taskId.Value, NotificationType.SharedCompanyDocumentAcknowledgementOverdue, cancellationToken);
            }

            tasksCancelledCount = await taskCanceller.CancelAllBySourceEntityAsync(
                document.CompanyId, document.Id, TaskSource.Document, TaskActionType.Acknowledge, cancellationToken);
        }

        document.SetAcknowledgementSettings(
            request.RequiresAcknowledgement,
            request.AcknowledgementDueDate,
            request.AcknowledgementStatement,
            updatedBy,
            now);

        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            document,
            request.ExpectedVersion,
            "This document was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>(saveResult.Error);

        if (hasChanges)
        {
            var after = new
            {
                document.RequiresAcknowledgement,
                document.AcknowledgementDueDate,
                document.AcknowledgementStatement,
            };

            await auditPublisher.PublishAsync(new SharedCompanyDocumentAcknowledgementSettingsUpdatedAuditEvent(
                document.CompanyId,
                document.Id,
                document.Title,
                before,
                after,
                updatedBy,
                now), cancellationToken);
        }

        if (isWithdrawal)
        {
            await auditPublisher.PublishAsync(new SharedCompanyDocumentAcknowledgementWithdrawnAuditEvent(
                document.CompanyId,
                document.Id,
                document.Title,
                tasksCancelledCount,
                notificationsRemovedCount,
                updatedBy,
                now), cancellationToken);
        }

        return Result.Success(new UpdateSharedCompanyDocumentAcknowledgementSettingsResponse(
            document.Id,
            document.CompanyId,
            document.RequiresAcknowledgement,
            document.AcknowledgementDueDate,
            document.AcknowledgementStatement,
            document.UpdatedAt,
            document.Version));
    }
}
