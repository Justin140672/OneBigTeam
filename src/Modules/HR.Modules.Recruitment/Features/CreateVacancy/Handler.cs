using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class CreateVacancyHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<CreateVacancyResponse>> HandleAsync(
        CreateVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var vacancy = Vacancy.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.DepartmentId,
            request.Title,
            request.Description,
            request.Location,
            request.HiringManagerId,
            now);

        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateVacancyResponse(
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
