namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed record ListPublishedSharedCompanyDocumentsResponse(IReadOnlyList<PublishedSharedCompanyDocumentItem> Items);

// Deliberately minimal compared to SharedCompanyDocumentListItem (the HR-facing list item) —
// no Status/Version/UpdatedBy, since every item here is already known to be Published.
internal sealed record PublishedSharedCompanyDocumentItem(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    DateTimeOffset? MyAcknowledgedAt,
    DateTimeOffset? PublishedAt,
    // One of SharedCompanyDocumentAcknowledgementStatusCalculator's four canonical values
    // ("Pending", "Completed", "Overdue", "Not Required") — the single source of truth for this
    // document's acknowledgement status from the calling employee's perspective, so callers (the
    // My Documents tab) don't need to re-derive it themselves from the raw fields above.
    string AcknowledgementStatus);
