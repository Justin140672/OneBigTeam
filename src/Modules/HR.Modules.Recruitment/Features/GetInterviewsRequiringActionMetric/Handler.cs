using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

/// <summary>
/// DSH-04 — "Interviews requiring action".
///
/// <para><b>Business definition.</b> The count of interviews that have already started or finished
/// (scheduled at or before the end of the current UTC day) but still have no recorded outcome
/// (<c>outcome = Pending</c>). These are the interviews genuinely blocking the pipeline: someone
/// needs to record how they went. Cancelled and completed (Passed / Failed / NoShow) interviews are
/// excluded, and interviews scheduled for later than today are excluded because no action is due yet.</para>
///
/// <para>This replaces the previous dashboard proxy, which was simply "count of interviews scheduled
/// today" — that number both missed overdue interviews from previous days and wrongly included
/// today's not-yet-happened interviews.</para>
///
/// Company scope enforced by the <c>{companyId}</c> route + <c>company_id</c> filter on every query.
/// </summary>
internal sealed class GetInterviewsRequiringActionMetricHandler(
    RecruitmentDbContext db, IClock clock, IPositionProfileReader positionProfileReader)
{
    public async Task<GetInterviewsRequiringActionMetricResponse> HandleAsync(
        GetInterviewsRequiringActionMetricRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var endOfToday = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);

        var rows = await (
                from interview in db.Interviews.AsNoTracking()
                join application in db.Applications.AsNoTracking() on interview.ApplicationId equals application.Id
                join candidate in db.Candidates.AsNoTracking() on application.CandidateId equals candidate.Id
                join vacancy in db.Vacancies.AsNoTracking() on application.VacancyId equals vacancy.Id
                where interview.CompanyId == request.CompanyId
                   && interview.Outcome == InterviewOutcome.Pending
                   && interview.ScheduledAt < endOfToday
                orderby interview.ScheduledAt
                select new
                {
                    InterviewId = interview.Id,
                    ApplicationId = application.Id,
                    CandidateId = candidate.Id,
                    CandidateName = candidate.FirstName + " " + candidate.LastName,
                    VacancyId = vacancy.Id,
                    vacancy.AdvertTitle,
                    vacancy.PositionProfileId,
                    interview.ScheduledAt,
                    interview.Location,
                })
            .ToListAsync(cancellationToken);

        var positionProfileIds = rows.Select(r => r.PositionProfileId).Distinct().ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        var items = rows
            .Select(r => new InterviewRequiringActionItem(
                r.InterviewId,
                r.ApplicationId,
                r.CandidateId,
                r.CandidateName,
                r.VacancyId,
                r.AdvertTitle ?? positionProfilesById.GetValueOrDefault(r.PositionProfileId)?.Title ?? "(untitled)",
                r.ScheduledAt,
                r.Location))
            .ToList();

        return new GetInterviewsRequiringActionMetricResponse(items.Count, items);
    }
}
