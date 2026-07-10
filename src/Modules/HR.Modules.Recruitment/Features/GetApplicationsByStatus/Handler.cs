using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed class GetApplicationsByStatusHandler(RecruitmentDbContext dbContext)
{
    public async Task<GetApplicationsByStatusResponse> HandleAsync(
        GetApplicationsByStatusRequest request,
        CancellationToken cancellationToken)
    {
        var items = await (
                from a in dbContext.Applications.AsNoTracking()
                join c in dbContext.Candidates.AsNoTracking() on a.CandidateId equals c.Id
                join v in dbContext.Vacancies.AsNoTracking() on a.VacancyId equals v.Id
                where a.CompanyId == request.CompanyId && a.Status == request.Status
                orderby a.AppliedAt descending
                select new ApplicationByStatusItem(
                    a.Id,
                    c.Id,
                    c.FirstName + " " + c.LastName,
                    c.Email,
                    v.Id,
                    v.Title,
                    a.AppliedAt))
            .ToListAsync(cancellationToken);

        return new GetApplicationsByStatusResponse(items);
    }
}
