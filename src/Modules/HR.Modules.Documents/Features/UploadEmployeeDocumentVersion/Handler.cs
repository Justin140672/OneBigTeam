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
