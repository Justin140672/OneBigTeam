using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed class GetVacancyHandler(RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
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

        var positionProfile = await positionProfileReader.GetSummaryAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        var applicationCount = await db.Applications
            .AsNoTracking()
            .CountAsync(a => a.VacancyId == vacancy.Id, cancellationToken);

        return Result.Success(new GetVacancyResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.PositionProfileId,
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.Status,
            vacancy.HiringManagerId,
            vacancy.AssignedRecruiterId,
            vacancy.IsAdvertisedInternally,
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt,
            positionProfile?.Title,
            positionProfile?.DepartmentId,
            positionProfile?.IsActive,
            vacancy.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
            positionProfile?.LocationName,
            applicationCount,
            UpdateVacancyHandler.CanChangePositionProfile(vacancy.Status, applicationCount),
            vacancy.Version,
            vacancy.EmploymentTypeId));
    }
}
