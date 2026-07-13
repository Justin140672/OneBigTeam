namespace HR.Modules.Documents.Features.GetSharedCompanyDocument;

internal sealed record GetSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
