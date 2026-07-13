namespace HR.Modules.Documents.Features.GetPublishedSharedCompanyDocument;

internal sealed record GetPublishedSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
