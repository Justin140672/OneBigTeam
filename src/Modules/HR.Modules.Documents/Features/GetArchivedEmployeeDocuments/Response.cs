namespace HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;

internal sealed record GetArchivedEmployeeDocumentsResponse(
    IReadOnlyList<ArchivedEmployeeDocumentListItem> Items);

internal sealed record ArchivedEmployeeDocumentListItem(
    Guid EmployeeDocumentId,
    string Title,
    string DocumentTypeName,
    Guid ArchivedByUserId,
    DateTimeOffset ArchivedAt,
    string? ArchiveReason,
    DateTimeOffset CreatedAt);
