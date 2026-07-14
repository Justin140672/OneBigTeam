namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocumentVersion;

internal sealed record DownloadSharedCompanyDocumentVersionRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public int VersionNumber { get; init; }
}
