using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetUpcomingInterviews;

internal sealed class GetUpcomingInterviewsHandler(
    RecruitmentDbContext db, IClock clock, IPositionProfileReader positionProfileReader)
{
    private const int MaxItems = 15;

    public async Task<GetUpcomingInterviewsResponse> HandleAsync(
        GetUpcomingInterviewsRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var rows = await (
            from interview in db.Interviews.AsNoTracking()
            join application in db.Applications.AsNoTracking() on interview.ApplicationId equals application.Id
            join candidate in db.Candidates.AsNoTracking() on application.CandidateId equals candidate.Id
            join vacancy in db.Vacancies.AsNoTracking() on application.VacancyId equals vacancy.Id
            where interview.CompanyId == request.CompanyId
               && interview.Outcome == InterviewOutcome.Pending
               && interview.ScheduledAt >= now
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
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        // Batch cross-module read for the pure-display "VacancyTitle" — same pattern/rationale as
        // GetApplicationsByStatusHandler.
        var positionProfileIds = rows
            .Select(r => r.PositionProfileId)
            .Distinct()
            .ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        var items = rows
            .Select(r =>
            {
                var positionProfile = positionProfilesById.GetValueOrDefault(r.PositionProfileId);

                return new UpcomingInterviewItem(
                    r.InterviewId,
                    r.ApplicationId,
                    r.CandidateId,
                    r.CandidateName,
                    r.VacancyId,
                    r.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
                    r.ScheduledAt,
                    r.Location);
            })
            .ToList();

        return new GetUpcomingInterviewsResponse(items);
    }
}
