using HR.Modules.Tasks.Contracts;
using Hangfire;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;

/// <summary>
/// DOC-05: uploads a replacement file for an existing employee document (e.g. renewing an expired
/// passport/visa/licence) as a NEW EmployeeDocument row linked to the one it replaces via
/// PreviousVersionId, rather than mutating the existing row or creating an unrelated record (the
/// bug this ticket exists to fix). The previous row is left completely untouched other than
/// flipping IsLatestVersion to false — see EmployeeDocument.SupersedeAsPreviousVersion — so its
/// own audit trail and any archive state remain intact.
/// </summary>
internal sealed class UploadEmployeeDocumentVersionHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    ITaskCompleter taskCompleter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<UploadEmployeeDocumentVersionResponse>> HandleAsync(
        UploadEmployeeDocumentVersionRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var previous = await db.EmployeeDocuments
            .FirstOrDefaultAsync(
                ed => ed.Id == request.EmployeeDocumentId
                   && ed.CompanyId == request.CompanyId
                   && ed.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (previous is null)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(
                Error.NotFound($"Employee document '{request.EmployeeDocumentId}' was not found."));

        if (!previous.IsLatestVersion)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(
                Error.Conflict("Only the latest version of a document can have a new version uploaded."));

        if (previous.IsArchived)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(
                Error.Conflict("Archived documents cannot have new versions uploaded."));

        var previousDocument = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == previous.DocumentId, cancellationToken);

        if (previousDocument is null)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(
                Error.NotFound($"Document '{previous.DocumentId}' was not found."));

        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(
                dt => dt.Id == previousDocument.DocumentTypeId && dt.CompanyId == request.CompanyId,
                cancellationToken);

        if (documentType is null)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(
                Error.NotFound($"Document type '{previousDocument.DocumentTypeId}' was not found."));

        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        // Virus scanning happens asynchronously via ScanUploadedFileJob (enqueued below) rather
        // than inline — the row is stored with ScanStatus = Pending.
        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadEmployeeDocumentVersionResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}/{request.EmployeeId}",
            cancellationToken);

        var now = clock.UtcNowOffset();

        // Title/description are carried forward from the previous version's Document row — a
        // renewal is still "the same document" from the employee/HR point of view, just a new
        // file with a new issue/expiry date.
        var newDocument = Document.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            previousDocument.Title,
            previousDocument.Description,
            documentType.Id,
            file.FileName,
            file.Length,
            file.ContentType,
            storageKey,
            expiryDate: null,
            uploadedBy,
            now);

        // A new version starts a fresh expiry-reminder schedule naturally: EmployeeDocument.Create
        // leaves every ExpiryReminder*SentAt / ExpiringSoonNotifiedAt / ExpiredNotifiedAt column
        // null on the new row, the same reset DOC-03's UpdateExpiryDate performs explicitly — no
        // separate reset call is needed here because this is a brand new row, not an in-place edit.
        var newVersion = EmployeeDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            newDocument.Id,
            uploadedBy,
            now,
            issueDate:  request.IssueDate,
            expiryDate: request.ExpiryDate,
            previousVersionId: previous.Id);

        previous.SupersedeAsPreviousVersion(now);

        db.Documents.Add(newDocument);
        db.EmployeeDocuments.Add(newVersion);

        // DOC-05: any outstanding request for this employee/document type is superseded by the
        // new version — mirrors UploadRequestedDocumentHandler's fulfillment logic for the
        // original-upload path.
        var outstandingRequest = await db.DocumentRequests
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId
                  && r.EmployeeId == request.EmployeeId
                  && r.DocumentTypeId == documentType.Id
                  && r.Status == DocumentRequestStatus.Requested,
                cancellationToken);

        outstandingRequest?.MarkUploaded(request.EmployeeId, now);

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

        if (outstandingRequest is not null)
        {
            await taskCompleter.CompleteBySourceEntityAsync(
                request.CompanyId,
                outstandingRequest.Id,
                TaskSource.Document,
                TaskActionType.Upload,
                uploadedBy,
                cancellationToken);

            await auditPublisher.PublishAsync(new DocumentRequestFulfilledAuditEvent(
                request.CompanyId,
                outstandingRequest.Id,
                request.EmployeeId,
                documentType.Name,
                uploadedBy,
                now), cancellationToken);
        }

        await auditPublisher.PublishAsync(new EmployeeDocumentVersionUploadedAuditEvent(
            request.CompanyId,
            newVersion.Id,
            previous.Id,
            request.EmployeeId,
            newDocument.Title,
            documentType.Name,
            newDocument.FileName,
            newDocument.FileSize,
            newVersion.IssueDate,
            newVersion.ExpiryDate,
            uploadedBy,
            now), cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new EmployeeDocumentUploadedIntegrationEvent(
                newDocument.CompanyId, request.EmployeeId, newVersion.Id, documentType.Name, now),
            cancellationToken);

        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.Document, newDocument.Id, newDocument.CompanyId, null));

        return Result.Success(new UploadEmployeeDocumentVersionResponse(
            newDocument.Id,
            newVersion.Id,
            previous.Id,
            newDocument.CompanyId,
            newDocument.EmployeeId!.Value,
            newDocument.Title,
            newDocument.FileName,
            newDocument.FileSize,
            newDocument.ContentType,
            newDocument.DocumentTypeId,
            newVersion.IssueDate,
            newVersion.ExpiryDate,
            newDocument.CreatedAt));
    }
}
