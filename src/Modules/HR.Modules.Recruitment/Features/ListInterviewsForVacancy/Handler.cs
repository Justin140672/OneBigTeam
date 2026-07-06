using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListInterviewsForVacancy;

internal sealed class ListInterviewsForVacancyHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListInterviewsForVacancyResponse>> HandleAsync(
        ListInterviewsForVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var items = await (
            from i in db.Interviews.AsNoTracking()
            join a in db.Applications.AsNoTracking() on i.ApplicationId equals a.Id
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            where i.CompanyId == request.CompanyId
               && a.VacancyId == request.VacancyId
            orderby i.ScheduledAt descending
            select new InterviewListItem(
                i.Id,
                i.ApplicationId,
                c.Id,
                c.FirstName,
                c.LastName,
                i.InterviewerEmployeeId,
                i.ScheduledAt,
                i.DurationMinutes,
                i.Location,
                i.Outcome,
                i.Notes))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListInterviewsForVacancyResponse(items));
    }
}
