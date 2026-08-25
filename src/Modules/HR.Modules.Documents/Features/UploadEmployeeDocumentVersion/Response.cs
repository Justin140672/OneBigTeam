namespace HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;

internal sealed record UploadEmployeeDocumentVersionResponse(
    Guid DocumentId,
    Guid EmployeeDocumentId,
    Guid PreviousVersionId,
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
