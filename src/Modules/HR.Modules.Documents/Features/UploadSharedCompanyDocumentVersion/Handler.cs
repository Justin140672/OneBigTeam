using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;

internal sealed class UploadSharedCompanyDocumentVersionHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    IVirusScanService virusScanner,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    ITaskCreator taskCreator,
    INotificationWriter notificationWriter,
    IClock clock)
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

        // Supported file type + maximum file size.
        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(validationResult.Error);

        // Safe storage filename — strip any directory component a crafted FileName (e.g.
        // "../../evil.pdf" or "..\..\evil.pdf") might carry, so it can never escape the storage
        // folder the same way the file-name segment of the storage key otherwise would. Split on
        // both separators explicitly rather than relying on Path.GetFileName, whose separator
        // handling is platform-dependent (it only treats '\' as a separator on Windows).
        var safeFileName = file.FileName.Split(['/', '\\']).Last();
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(
                Error.Validation("File name is not valid."));
        }

        await using var fileStream = file.OpenReadStream();

        var scanResult = await virusScanner.ScanAsync(fileStream, safeFileName, cancellationToken);
        if (!scanResult.IsClean)
            return Result.Failure<UploadSharedCompanyDocumentVersionResponse>(
                Error.Validation($"File was rejected: {scanResult.ThreatName}."));

        fileStream.Seek(0, SeekOrigin.Begin);

        // Verify file content matches the declared content type (prevents extension/MIME spoofing).
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

        var versionNote = request.VersionNote.Trim();

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
            effectiveDate: document.EffectiveDate);

        db.SharedCompanyDocumentVersions.Add(version);

        if (document.RequiresAcknowledgement && document.Status == SharedCompanyDocumentStatus.Published)
        {
            if (request.RequiresReacknowledgement)
            {
                var eligibleEmployeeIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(
                    request.CompanyId, document.Id, cancellationToken);

                // Known v1 limitation: an employee who still has an open Acknowledge task from the previous version will end up with two open tasks for this document — ITaskCanceller has no bulk-cancel-by-source-entity capability (it only cancels the first match), so stale per-version tasks aren't cleaned up here. Completing either task still correctly records acknowledgement of the current version, since AcknowledgeSharedCompanyDocumentHandler reads VersionNumber fresh at acknowledge-time.
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
                        assignedUserId:     null,
                        sourceEntityId:     document.Id,
                        cancellationToken);

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
            // Best-effort: remove the already-uploaded file so it doesn't become an orphan.
            try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }
            throw;
        }

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
