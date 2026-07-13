namespace HR.Modules.Documents.Features.GetPublishedSharedCompanyDocument;

// Deliberately excludes every management-only field GetSharedCompanyDocumentResponse has:
// no VersionHistory, no AudienceDescription, no aggregate AcknowledgementProgress, no
// CreatedBy/UpdatedBy — only the caller's own acknowledgement state.
internal sealed record GetPublishedSharedCompanyDocumentResponse(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement,
    DateTimeOffset? MyAcknowledgedAt);
