using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed class ListVacanciesHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListVacanciesResponse>> HandleAsync(
        ListVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == request.CompanyId);

        if (request.Status.HasValue)
            query = query.Where(v => v.Status == request.Status.Value);

        if (request.DepartmentId.HasValue)
            query = query.Where(v => v.DepartmentId == request.DepartmentId.Value);

        var items = await query
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new VacancyListItem(
                v.Id,
                v.DepartmentId,
                v.Title,
                v.Location,
                v.Status,
                v.HiringManagerId,
                v.OpenedAt,
                v.ClosedAt,
                v.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListVacanciesResponse(items));
    }
}
