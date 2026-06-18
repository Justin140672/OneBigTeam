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
    IClock clock)
{
    public async Task<Result<UploadEmployeeDocumentResponse>> HandleAsync(
        UploadEmployeeDocumentRequest request,
        Guid uploadedBy,
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

        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(
                dt => dt.Id == request.DocumentTypeId &&
                      dt.CompanyId == request.CompanyId &&
                      dt.IsActive,
                cancellationToken);

        if (documentType is null)
            return Result.Failure<UploadEmployeeDocumentResponse>(
                Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));

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
            request.Title,
            request.Description,
            request.DocumentTypeId,
            file.FileName,
            file.Length,
            file.ContentType,
            storageKey,
            request.ExpiryDate,
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
        await db.SaveChangesAsync(cancellationToken);

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
            document.ExpiryDate,
            document.CreatedAt));
    }
}
