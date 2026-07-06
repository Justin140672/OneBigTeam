namespace HR.Modules.Recruitment.Features.DeleteCandidateDocument;

internal sealed record DeleteCandidateDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
    public Guid DocumentId { get; init; }
}
