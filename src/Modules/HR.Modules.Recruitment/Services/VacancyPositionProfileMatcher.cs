using HR.Modules.Recruitment.Domain;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Services;

internal enum VacancyPositionProfileMatchOutcome
{
    /// <summary>Exactly one active position profile matched company + department + title.</summary>
    Matched,

    /// <summary>No active position profile matched company + department + title.</summary>
    Unmatched,

    /// <summary>More than one active position profile matched — needs HR review, never auto-assigned.</summary>
    Ambiguous,
}

internal sealed record VacancyPositionProfileMatchResult(
    Guid VacancyId,
    VacancyPositionProfileMatchOutcome Outcome,
    Guid? MatchedPositionProfileId,
    IReadOnlyList<Guid> CandidatePositionProfileIds);

/// <summary>
/// Matches a vacancy without a position profile to an existing, active position profile using
/// company + title (case-insensitive, trimmed). Reused by both the one-time/repeatable admin backfill
/// action (ApplyPositionProfileMatches) and the read-only HR review listing
/// (GetVacanciesNeedingPositionProfileReview). Never auto-assigns when more than one candidate
/// matches — that is surfaced as Ambiguous for a human to resolve via AssignVacancyPositionProfile.
/// </summary>
/// <remarks>
/// Judgment call (Refactor Duplicate Vacancy Fields): this used to also filter by
/// Vacancy.DepartmentId, but that column has been removed entirely — a vacancy's department is now
/// only ever known via its linked Position Profile, so a vacancy that doesn't have one yet (the only
/// case this matcher runs for) has no department to filter by. IPositionProfileReader.FindActiveMatchesAsync
/// already supports this exact scenario: passing a null departmentId performs a company-wide,
/// title-only match, per that method's own doc ("lets callers detect ambiguity across departments for
/// records that have no department of their own"). This may surface more Ambiguous results than
/// before for companies with same-titled roles in different departments — those now correctly require
/// manual review via AssignVacancyPositionProfile rather than silently auto-matching against the wrong
/// department.
/// </remarks>
internal sealed class VacancyPositionProfileMatcher(IPositionProfileReader positionProfileReader)
{
    public async Task<VacancyPositionProfileMatchResult> MatchAsync(Vacancy vacancy, CancellationToken cancellationToken)
    {
        // AdvertTitle is optional, so a legacy vacancy without one (and without a Position Profile
        // to fall back to, since that's the whole reason this matcher is running) simply cannot be
        // text-matched — treat it as Unmatched rather than calling FindActiveMatchesAsync with a null
        // title.
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
