using HR.Modules.Tasks.Contracts;
using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;

internal sealed class UploadSharedCompanyDocumentVersionHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    ITaskCreator taskCreator,
    ITaskCanceller taskCanceller,
    INotificationWriter notificationWriter,
    IAuditEventPublisher auditPublisher,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<UploadSharedCompanyDocumentVersionHandler>? logger = null)
{
    public async Task<Result<UploadSharedCompanyDocumentVersionResponse>> HandleAsync(
        UploadSharedCompanyDocumentVersionRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status == SharedCompanyDocumentStatus.Archived)
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(
                Error.Conflict("Archived documents cannot have new versions uploaded."));

        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(validationResult.Error);

        var safeFileName = file.FileName.Split(['/', '\\']).Last();
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(
                Error.Validation("File name is not valid."));
        }

        await using var fileStream = file.OpenReadStream();

        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            safeFileName,
            file.ContentType,
            $"{request.CompanyId}/shared-documents",
            cancellationToken);

        var now = clock.UtcNowOffset();
        document.ReplaceFile(storageKey, safeFileName, file.Length, file.ContentType, uploadedBy, now);

        if (document.RequiresAcknowledgement && request.RequiresReacknowledgement &&
            !string.IsNullOrWhiteSpace(request.AcknowledgementStatement))
        {
            document.SetAcknowledgementSettings(
                document.RequiresAcknowledgement,
                document.AcknowledgementDueDate,
                request.AcknowledgementStatement,
                uploadedBy,
                now);
        }

        var versionNote = request.VersionNote.Trim();

        var previousVersionStatement = await db.SharedCompanyDocumentVersions
            .Where(v => v.SharedCompanyDocumentId == document.Id && v.VersionNumber == document.VersionNumber - 1)
            .Select(v => v.AcknowledgementStatement)
            .FirstOrDefaultAsync(cancellationToken);

        var resolvedAcknowledgementStatement = !string.IsNullOrWhiteSpace(request.AcknowledgementStatement)
            ? request.AcknowledgementStatement
            : previousVersionStatement;

        var version = SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(),
            request.CompanyId,
            document.Id,
            document.VersionNumber,
            storageKey,
            safeFileName,
            file.Length,
            file.ContentType,
            uploadedBy,
            now,
            versionNote: versionNote,
            requiresAcknowledgement: document.RequiresAcknowledgement && request.RequiresReacknowledgement,
            effectiveDate: document.EffectiveDate,
            acknowledgementStatement: resolvedAcknowledgementStatement);

        db.SharedCompanyDocumentVersions.Add(version);

        if (document.RequiresAcknowledgement && document.Status == SharedCompanyDocumentStatus.Published)
        {
            if (request.RequiresReacknowledgement)
            {
                var eligibleEmployeeIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(
                    request.CompanyId, document.Id, cancellationToken);

                await taskCanceller.CancelAllBySourceEntityAsync(
                    request.CompanyId, document.Id, TaskSource.Document, TaskActionType.Acknowledge, cancellationToken);

                foreach (var employeeId in eligibleEmployeeIds)
                {
                    await taskCreator.CreateAsync(
                        request.CompanyId,
                        createdBy:          uploadedBy,
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
                }
            }
            else
            {
                var priorAcknowledgements = await db.SharedCompanyDocumentAcknowledgements
                    .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber - 1)
                    .ToListAsync(cancellationToken);

                foreach (var priorAcknowledgement in priorAcknowledgements)
                {
                    db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
                        Guid.NewGuid(),
                        request.CompanyId,
                        document.Id,
                        priorAcknowledgement.EmployeeId,
                        document.VersionNumber,
                        acknowledgementStatement: priorAcknowledgement.AcknowledgementStatement,
                        taskId: null,
                        isConfirmed: priorAcknowledgement.IsConfirmed,
                        now: priorAcknowledgement.AcknowledgedAt));
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await StorageCleanup.TryDeleteAsync(
                storage, logger, "UploadSharedCompanyDocumentVersion", storageKey, request.CompanyId, document.Id);
            throw;
        }

        await auditPublisher.PublishAsync(new SharedCompanyDocumentVersionUploadedAuditEvent(
            document.CompanyId, document.Id, document.Title, safeFileName, file.Length, document.VersionNumber,
            versionNote, request.RequiresReacknowledgement, uploadedBy, now), cancellationToken);

        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.SharedCompanyDocument, document.Id, document.CompanyId, null));
        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.SharedCompanyDocumentVersion, version.Id, document.CompanyId, null));

        return Result.Success(new UploadSharedCompanyDocumentVersionResponse(
            document.Id,
            document.CompanyId,
            document.VersionNumber,
            document.FileName,
            document.FileSize,
            versionNote,
            request.RequiresReacknowledgement,
            uploadedBy,
            now));
    }
}
