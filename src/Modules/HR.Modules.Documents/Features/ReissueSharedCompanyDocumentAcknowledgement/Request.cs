namespace HR.Modules.Documents.Features.ReissueSharedCompanyDocumentAcknowledgement;

internal sealed record ReissueSharedCompanyDocumentAcknowledgementRequest(
    Guid CompanyId,
    Guid DocumentId);
