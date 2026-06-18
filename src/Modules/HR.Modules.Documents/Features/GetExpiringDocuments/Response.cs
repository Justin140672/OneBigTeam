using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.GetExpiringDocuments;

internal sealed record GetExpiringDocumentsResponse(
    IReadOnlyList<ExpiringDocumentItem> Items);

internal sealed record ExpiringDocumentItem(
    Guid   EmployeeDocumentId,
    Guid   EmployeeId,
    string Title,
    string DocumentTypeName,
    DateOnly ExpiryDate,
    DocumentExpiryStatus ExpiryStatus);
