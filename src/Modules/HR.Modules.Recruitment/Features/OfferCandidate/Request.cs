namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed record OfferCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
}
