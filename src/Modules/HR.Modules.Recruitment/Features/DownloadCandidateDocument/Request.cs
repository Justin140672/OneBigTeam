namespace HR.Modules.Recruitment.Features.DownloadCandidateDocument;

internal sealed record DownloadCandidateDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
    public Guid DocumentId { get; init; }
}
