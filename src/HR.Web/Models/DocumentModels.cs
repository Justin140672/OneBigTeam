namespace HR.Web.Models;

public sealed record EmployeeDocumentListResponse(IReadOnlyList<EmployeeDocumentListItem> Items);

public sealed record EmployeeDocumentListItem(
    Guid     EmployeeDocumentId,
    string   Title,
    string   DocumentTypeName,
    string   Status,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    bool     IsAcknowledged,
    DateTimeOffset CreatedAt);

public sealed record EmployeeDocumentDetailResponse(string? DownloadUrl);

public sealed record DocumentTypeListResponse(IReadOnlyList<DocumentTypeItem> Items);

public sealed record DocumentTypeItem(Guid Id, string Name, string? Description, bool IsActive);
