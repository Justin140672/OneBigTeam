using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed record ListEmployeeDocumentsResponse(
    IReadOnlyList<EmployeeDocumentListItem> Items);

internal sealed record EmployeeDocumentListItem(
    Guid EmployeeDocumentId,
    string Title,
    string DocumentTypeName,
    DocumentStatus Status,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    bool IsAcknowledged,
    DateTimeOffset CreatedAt);
