using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal sealed class VacancyHiringManagerReader(RecruitmentDbContext db) : IVacancyHiringManagerReader
{
    public async Task<Guid?> GetHiringManagerIdForInterviewAsync(
        Guid companyId,
        Guid interviewId,
        CancellationToken cancellationToken)
    {
        var hiringManagerId = await (
            from i in db.Interviews.AsNoTracking()
            join a in db.Applications.AsNoTracking() on i.ApplicationId equals a.Id
            join v in db.Vacancies.AsNoTracking() on a.VacancyId equals v.Id
            where i.Id == interviewId && i.CompanyId == companyId
            select (Guid?)v.HiringManagerId)
            .SingleOrDefaultAsync(cancellationToken);

        return hiringManagerId;
    }
}
