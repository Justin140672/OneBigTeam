using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateVacancy;

internal sealed class UpdateVacancyHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<UpdateVacancyResponse>> HandleAsync(
        UpdateVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<UpdateVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var now = clock.UtcNowOffset();

        vacancy.UpdateDetails(
            request.DepartmentId,
            request.Title,
            request.Description,
            request.Location,
            request.HiringManagerId,
            now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateVacancyResponse(
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
