namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocument;

internal sealed record DownloadSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
