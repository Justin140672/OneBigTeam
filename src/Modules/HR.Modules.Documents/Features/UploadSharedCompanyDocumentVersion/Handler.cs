using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

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
    IBackgroundJobClient backgroundJobClient)
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

        // Virus scanning happens asynchronously via ScanUploadedFileJob (enqueued below) rather
        // than inline — the row is stored with ScanStatus = Pending.
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

        // Only route left to change the acknowledgement wording once a document is Published (see
        // UpdateSharedCompanyDocumentAcknowledgementSettingsHandler's post-publish lock) — applied
        // only when this version also requires re-acknowledgement, matching how re-triggering
        // acknowledgement tasks already only happens in that case.
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

        // Copy-forward: the version about to become "previous" is document.VersionNumber - 1,
        // since document.ReplaceFile() above already incremented VersionNumber for the new
        // version being created here. HR may explicitly override the wording for the new version
        // (request.AcknowledgementStatement); otherwise the previous version's own statement is
        // carried forward unchanged so every version always holds its own independent copy.
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

                // Cancel any still-open Acknowledge task from a previous version before creating
                // this version's tasks below — otherwise an employee who hadn't yet acknowledged
                // the prior version ends up with two open tasks for the same document. Completing
                // either task would still have correctly recorded acknowledgement of the current
                // version (AcknowledgeSharedCompanyDocumentHandler reads VersionNumber fresh at
                // acknowledge-time), but leaving the stale one open is confusing task-list clutter.
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
            // Best-effort: remove the already-uploaded file so it doesn't become an orphan.
            try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }
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
