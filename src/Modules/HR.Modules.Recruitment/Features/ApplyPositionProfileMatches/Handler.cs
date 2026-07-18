using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;

/// <summary>
/// LEGACY / dead-in-practice (judgement call, "Vacancy Location &amp; Mandatory Position Profile"
/// story): Vacancy.PositionProfileId is now NOT NULL at the domain and DB level, so no vacancy can
/// ever be without one under normal operation — this action will therefore never find anything to
/// assign. Retained (rather than removed) alongside GetVacanciesNeedingPositionProfileReview and
/// AssignVacancyPositionProfile in case a future real deployment somehow still needs a one-time
/// historical backfill before adopting the NOT NULL constraint. Short-circuits before touching the
/// database since the query it used to run (WHERE position_profile_id IS NULL) is no longer
/// expressible against a non-nullable Guid column/property.
/// </summary>
internal sealed class ApplyPositionProfileMatchesHandler(
    RecruitmentDbContext db,
    VacancyPositionProfileMatcher matcher,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public Task<Result<ApplyPositionProfileMatchesResponse>> HandleAsync(
        ApplyPositionProfileMatchesRequest request,
        CancellationToken cancellationToken)
    {
        _ = db;
        _ = matcher;
        _ = clock;
        _ = auditPublisher;
        return Task.FromResult(Result.Success(new ApplyPositionProfileMatchesResponse([])));
    }
}
