namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed record ListPublishedSharedCompanyDocumentsResponse(IReadOnlyList<PublishedSharedCompanyDocumentItem> Items);

// Deliberately minimal compared to SharedCompanyDocumentListItem (the HR-facing list item) —
// no Status/Version/UpdatedBy, since every item here is already known to be Published.
internal sealed record PublishedSharedCompanyDocumentItem(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate);
