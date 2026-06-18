using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.GetEmployeeDocument;

internal sealed record GetEmployeeDocumentResponse(
    Guid EmployeeDocumentId,
    Guid DocumentId,
    Guid CompanyId,
    Guid EmployeeId,
    string Title,
    string? Description,
    string FileName,
    long FileSize,
    string ContentType,
    Guid DocumentTypeId,
    string DocumentTypeName,
    DocumentStatus Status,
    DateOnly? DocumentExpiryDate,
    Guid UploadedBy,
    Guid AddedBy,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset CreatedAt,
    Uri DownloadUrl);
