using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

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
