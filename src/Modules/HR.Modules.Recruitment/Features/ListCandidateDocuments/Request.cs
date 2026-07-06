namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed record ListCandidateDocumentsRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
}
