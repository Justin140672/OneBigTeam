using HR.Modules.Recruitment.Domain;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Services;

internal enum VacancyPositionProfileMatchOutcome
{
    Matched,

    Unmatched,

    Ambiguous,
}

internal sealed record VacancyPositionProfileMatchResult(
    Guid VacancyId,
    VacancyPositionProfileMatchOutcome Outcome,
    Guid? MatchedPositionProfileId,
    IReadOnlyList<Guid> CandidatePositionProfileIds);

internal sealed class VacancyPositionProfileMatcher(IPositionProfileReader positionProfileReader)
{
    public async Task<VacancyPositionProfileMatchResult> MatchAsync(Vacancy vacancy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(vacancy.AdvertTitle))
            return new VacancyPositionProfileMatchResult(vacancy.Id, VacancyPositionProfileMatchOutcome.Unmatched, null, []);

        var candidates = await positionProfileReader.FindActiveMatchesAsync(
            vacancy.CompanyId, departmentId: null, vacancy.AdvertTitle, cancellationToken);

        return candidates.Count switch
        {
            0 => new VacancyPositionProfileMatchResult(vacancy.Id, VacancyPositionProfileMatchOutcome.Unmatched, null, candidates),
            1 => new VacancyPositionProfileMatchResult(vacancy.Id, VacancyPositionProfileMatchOutcome.Matched, candidates[0], candidates),
            _ => new VacancyPositionProfileMatchResult(vacancy.Id, VacancyPositionProfileMatchOutcome.Ambiguous, null, candidates),
        };
    }
}
