namespace HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;

internal sealed record ArchiveSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
