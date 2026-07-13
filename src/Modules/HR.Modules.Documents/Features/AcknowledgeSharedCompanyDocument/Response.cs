namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed record AcknowledgeSharedCompanyDocumentResponse(
    Guid SharedCompanyDocumentId,
    int VersionNumber,
    DateTimeOffset AcknowledgedAt);
