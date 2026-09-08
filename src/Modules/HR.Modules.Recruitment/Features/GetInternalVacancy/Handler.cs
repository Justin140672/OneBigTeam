using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetInternalVacancy;

internal sealed class GetInternalVacancyHandler(RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
{
    public async Task<Result<GetInternalVacancyResponse>> HandleAsync(
        GetInternalVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId
                    && v.CompanyId == request.CompanyId
                    && v.Status == VacancyStatus.Open
                    && v.IsAdvertisedInternally,
                cancellationToken);

        // A draft/closed/non-advertised/cross-company vacancy is simply "not found" to employees —
        // no information disclosure.
        if (vacancy is null)
            return Result.Failure<GetInternalVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var positionProfile = await positionProfileReader.GetSummaryAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        return Result.Success(new GetInternalVacancyResponse(
            vacancy.Id,
            vacancy.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
            vacancy.AdvertDescription ?? positionProfile?.Description,
            positionProfile?.DepartmentName,
            positionProfile?.LocationName,
            null,
            null,
            vacancy.OpenedAt));
    }
}
