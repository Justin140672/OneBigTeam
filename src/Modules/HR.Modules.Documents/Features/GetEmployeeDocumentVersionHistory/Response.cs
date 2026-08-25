namespace HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

internal sealed record GetEmployeeDocumentVersionHistoryResponse(
    IReadOnlyList<EmployeeDocumentVersionHistoryItem> Versions);

internal sealed record EmployeeDocumentVersionHistoryItem(
    Guid EmployeeDocumentId,
    Guid? PreviousVersionId,
    bool IsLatestVersion,
    string FileName,
    long FileSize,
    Guid UploadedBy,
    DateTimeOffset UploadedAt,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    bool IsArchived);
