namespace HR.Modules.Documents.Features.UploadEmployeeDocument;

internal sealed record UploadEmployeeDocumentResponse(
    Guid DocumentId,
    Guid EmployeeDocumentId,
    Guid CompanyId,
    Guid EmployeeId,
    string Title,
    string FileName,
    long FileSize,
    string ContentType,
    Guid DocumentTypeId,
    DateOnly? ExpiryDate,
    DateTimeOffset CreatedAt);
