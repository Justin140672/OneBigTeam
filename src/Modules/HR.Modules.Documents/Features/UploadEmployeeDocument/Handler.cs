using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadEmployeeDocument;

internal sealed class UploadEmployeeDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    IVirusScanService virusScanner,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    // Backward-compatible overload used by existing tests — treats the caller as a manager.
    public Task<Result<UploadEmployeeDocumentResponse>> HandleAsync(
        UploadEmployeeDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
        => HandleAsync(request, uploadedBy, isManagerUpload: true, cancellationToken);

    public async Task<Result<UploadEmployeeDocumentResponse>> HandleAsync(
        UploadEmployeeDocumentRequest request,
        Guid uploadedBy,
        bool isManagerUpload,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadEmployeeDocumentResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        var scanResult = await virusScanner.ScanAsync(fileStream, file.FileName, cancellationToken);
        if (!scanResult.IsClean)
            return Result.Failure<UploadEmployeeDocumentResponse>(
                Error.Validation($"File was rejected: {scanResult.ThreatName}."));

        fileStream.Seek(0, SeekOrigin.Begin);

        // Verify file content matches the declared content type (prevents extension/MIME spoofing).
        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadEmployeeDocumentResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(
                dt => dt.Id == request.DocumentTypeId &&
                      dt.CompanyId == request.CompanyId &&
                      dt.IsActive,
                cancellationToken);

        if (documentType is null)
            return Result.Failure<UploadEmployeeDocumentResponse>(
                Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));

        if (!isManagerUpload && !documentType.AllowEmployeeUpload)
            return Result.Failure<UploadEmployeeDocumentResponse>(
                Error.Validation($"Document type '{documentType.Name}' does not allow employee uploads."));

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}/{request.EmployeeId}",
            cancellationToken);

        var now = clock.UtcNowOffset();

        // ExpiryDate is intentionally NOT passed to Document — the canonical expiry for HR tracking
        // lives on EmployeeDocument. Document.ExpiryDate is reserved for document-level metadata
        // set through other workflows (e.g. document template management).
        var document = Document.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.Title,
            request.Description,
            request.DocumentTypeId,
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

        db.Documents.Add(document);
        db.EmployeeDocuments.Add(employeeDocument);

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
            isManagerUpload,
            now), cancellationToken);

        return Result.Success(new UploadEmployeeDocumentResponse(
            document.Id,
            employeeDocument.Id,
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
