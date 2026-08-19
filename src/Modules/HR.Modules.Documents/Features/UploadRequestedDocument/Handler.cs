using HR.Modules.Tasks.Contracts;
using Hangfire;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadRequestedDocument;

internal sealed class UploadRequestedDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    ITaskCompleter taskCompleter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<UploadRequestedDocumentResponse>> HandleAsync(
        UploadRequestedDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var documentRequest = await db.DocumentRequests
            .FirstOrDefaultAsync(
                r => r.Id == request.DocumentRequestId && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (documentRequest is null)
            return Result.Failure<UploadRequestedDocumentResponse>(
                Error.NotFound($"Document request '{request.DocumentRequestId}' was not found."));

        if (documentRequest.EmployeeId != request.EmployeeId)
            return Result.Failure<UploadRequestedDocumentResponse>(
                Error.Validation("Document request does not belong to this employee."));

        if (documentRequest.Status != DocumentRequestStatus.Requested)
            return Result.Failure<UploadRequestedDocumentResponse>(
                Error.Conflict($"Document request is not open (status: {documentRequest.Status})."));

        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(
                dt => dt.Id == documentRequest.DocumentTypeId
                   && dt.CompanyId == request.CompanyId
                   && dt.IsActive,
                cancellationToken);

        if (documentType is null)
            return Result.Failure<UploadRequestedDocumentResponse>(
                Error.NotFound($"Document type '{documentRequest.DocumentTypeId}' was not found."));

        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadRequestedDocumentResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        // Virus scanning happens asynchronously via ScanUploadedFileJob (enqueued below) rather
        // than inline — the row is stored with ScanStatus = Pending.
        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadRequestedDocumentResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}/{request.EmployeeId}",
            cancellationToken);

        var now = clock.UtcNowOffset();

        var document = Document.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            documentType.Id,
            file.FileName,
            file.Length,
            file.ContentType,
            storageKey,
            expiryDate: null,
            uploadedBy,
            now);

        var employeeDocument = EmployeeDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            document.Id,
            uploadedBy,
            now,
            issueDate:  request.IssueDate,
            expiryDate: request.ExpiryDate);

        documentRequest.MarkUploaded(request.EmployeeId, now);

        db.Documents.Add(document);
        db.EmployeeDocuments.Add(employeeDocument);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }
            throw;
        }

        await taskCompleter.CompleteBySourceEntityAsync(
            request.CompanyId,
            documentRequest.Id,
            TaskSource.Document,
            TaskActionType.Upload,
            uploadedBy,
            cancellationToken);

        await auditPublisher.PublishAsync(new DocumentUploadedAuditEvent(
            document.CompanyId,
            employeeDocument.Id,
            request.EmployeeId,
            document.Title,
            documentType.Name,
            document.FileName,
            document.FileSize,
            employeeDocument.IssueDate,
            employeeDocument.ExpiryDate,
            uploadedBy,
            IsManagerUpload: false,
            now), cancellationToken);

        await auditPublisher.PublishAsync(new DocumentRequestFulfilledAuditEvent(
            request.CompanyId,
            documentRequest.Id,
            request.EmployeeId,
            documentType.Name,
            uploadedBy,
            now), cancellationToken);

        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.Document, document.Id, document.CompanyId, null));

        return Result.Success(new UploadRequestedDocumentResponse(
            document.Id,
            employeeDocument.Id,
            documentRequest.Id,
            document.CompanyId,
            document.EmployeeId!.Value,
            document.Title,
            document.FileName,
            document.FileSize,
            document.ContentType,
            document.DocumentTypeId,
            employeeDocument.IssueDate,
            employeeDocument.ExpiryDate,
            document.CreatedAt));
    }
}
