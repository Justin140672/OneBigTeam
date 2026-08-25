using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

internal sealed record SearchEmployeeDocumentsResponse(
    IReadOnlyList<EmployeeDocumentSearchItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record EmployeeDocumentSearchItem(
    Guid EmployeeDocumentId,
    Guid EmployeeId,
    string EmployeeName,
    string Title,
    string FileName,
    Guid DocumentTypeId,
    string DocumentTypeName,
    DocumentStatus Status,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    bool IsAcknowledged,
    bool IsArchived,
    DateTimeOffset CreatedAt);
