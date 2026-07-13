using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetUpcomingInterviews;

internal sealed class GetUpcomingInterviewsHandler(RecruitmentDbContext db, IClock clock)
{
    private const int MaxItems = 15;

    public async Task<GetUpcomingInterviewsResponse> HandleAsync(
        GetUpcomingInterviewsRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var items = await (
            from interview in db.Interviews.AsNoTracking()
            join application in db.Applications.AsNoTracking() on interview.ApplicationId equals application.Id
            join candidate in db.Candidates.AsNoTracking() on application.CandidateId equals candidate.Id
            join vacancy in db.Vacancies.AsNoTracking() on application.VacancyId equals vacancy.Id
            where interview.CompanyId == request.CompanyId
               && interview.Outcome == InterviewOutcome.Pending
               && interview.ScheduledAt >= now
            orderby interview.ScheduledAt
            select new UpcomingInterviewItem(
                interview.Id,
                application.Id,
                candidate.Id,
                candidate.FirstName + " " + candidate.LastName,
                vacancy.Id,
                vacancy.Title,
                interview.ScheduledAt,
                interview.Location))
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        return new GetUpcomingInterviewsResponse(items);
    }
}
