using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed class ListApplicationsForVacancyHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListApplicationsForVacancyResponse>> HandleAsync(
        ListApplicationsForVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var query =
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            where a.CompanyId == request.CompanyId
               && a.VacancyId == request.VacancyId
            select new { a, c };

        if (request.Status.HasValue)
            query = query.Where(x => x.a.Status == request.Status.Value);

        var items = await query
            .OrderByDescending(x => x.a.AppliedAt)
            .Select(x => new ApplicationListItem(
                x.a.Id,
                x.a.CandidateId,
                x.c.FirstName,
                x.c.LastName,
                x.c.Email,
                x.a.Status,
                x.a.InterviewOutcome,
                x.a.AppliedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListApplicationsForVacancyResponse(items));
    }
}
