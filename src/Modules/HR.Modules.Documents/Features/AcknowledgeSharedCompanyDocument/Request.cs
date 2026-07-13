namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed record AcknowledgeSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
