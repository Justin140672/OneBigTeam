using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
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

        // Cross-module read: the linked Position Profile is owned by HR.Modules.Employees, so its
        // canonical role information is resolved through the narrow IPositionProfileReader contract
        // rather than a direct module reference. A deactivated/no-longer-findable profile resolves to
        // null here rather than failing the request — see GetVacancyResponse's remarks.
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
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt,
            positionProfile?.Title,
            positionProfile?.DepartmentId,
            positionProfile?.Description,
            positionProfile?.IsActive,
            vacancy.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
            positionProfile?.LocationName,
            applicationCount,
            UpdateVacancyHandler.CanChangePositionProfile(vacancy.Status, applicationCount)));
    }
}
