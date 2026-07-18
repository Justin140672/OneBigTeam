using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetVacanciesNeedingPositionProfileReview;

/// <summary>
/// LEGACY / dead-in-practice (judgement call, "Vacancy Location &amp; Mandatory Position Profile"
/// story): Vacancy.PositionProfileId is now NOT NULL at the domain and DB level, so no vacancy can
/// ever be without one under normal operation — this listing will therefore always return an empty
/// result. Retained (rather than removed) in case a future real deployment somehow still needs a
/// one-time historical backfill before adopting the NOT NULL constraint (e.g. importing legacy data
/// from an external system prior to this constraint being enforced). Short-circuits before touching
/// the database since the query it used to run (WHERE position_profile_id IS NULL) is no longer
/// expressible against a non-nullable Guid column/property.
/// </summary>
internal sealed class GetVacanciesNeedingPositionProfileReviewHandler(
    RecruitmentDbContext db,
    VacancyPositionProfileMatcher matcher)
{
    public Task<Result<GetVacanciesNeedingPositionProfileReviewResponse>> HandleAsync(
        GetVacanciesNeedingPositionProfileReviewRequest request,
        CancellationToken cancellationToken)
    {
        _ = db;
        _ = matcher;
        return Task.FromResult(Result.Success(
            new GetVacanciesNeedingPositionProfileReviewResponse([])));
    }
}
