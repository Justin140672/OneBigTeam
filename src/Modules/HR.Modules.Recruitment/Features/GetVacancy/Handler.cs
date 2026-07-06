using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed class GetVacancyHandler(RecruitmentDbContext db)
{
    public async Task<Result<GetVacancyResponse>> HandleAsync(
        GetVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<GetVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        return Result.Success(new GetVacancyResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.DepartmentId,
            vacancy.Title,
            vacancy.Description,
            vacancy.Location,
            vacancy.Status,
            vacancy.HiringManagerId,
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt));
    }
}
