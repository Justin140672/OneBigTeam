namespace HR.Modules.Documents.Features.ListDocumentTypes;

internal sealed record ListDocumentTypesResponse(IReadOnlyList<DocumentTypeListItem> Items);

internal sealed record DocumentTypeListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool AllowEmployeeUpload);
