namespace HR.Modules.Documents.Features.ExpireSharedCompanyDocument;

internal sealed record ExpireSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
