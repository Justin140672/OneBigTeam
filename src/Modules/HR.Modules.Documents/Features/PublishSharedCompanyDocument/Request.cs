namespace HR.Modules.Documents.Features.PublishSharedCompanyDocument;

internal sealed record PublishSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
