namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed record HireCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
}
