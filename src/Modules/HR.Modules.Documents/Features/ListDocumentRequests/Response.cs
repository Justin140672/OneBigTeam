namespace HR.Modules.Documents.Features.ListDocumentRequests;

internal sealed record ListDocumentRequestsResponse(
    IReadOnlyList<DocumentRequestListItem> Items);

internal sealed record DocumentRequestListItem(
    Guid     Id,
    string   DocumentTypeName,
    DateOnly? DueDate,
    string   Status);
