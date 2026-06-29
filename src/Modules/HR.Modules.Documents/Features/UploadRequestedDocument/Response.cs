namespace HR.Modules.Documents.Features.UploadRequestedDocument;

internal sealed record UploadRequestedDocumentResponse(
    Guid DocumentId,
    Guid EmployeeDocumentId,
    Guid DocumentRequestId,
    Guid CompanyId,
    Guid EmployeeId,
    string Title,
    string FileName,
    long FileSize,
    string ContentType,
    Guid DocumentTypeId,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    DateTimeOffset CreatedAt);
