using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed class ListInternalVacanciesHandler(RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
{
    public async Task<Result<ListInternalVacanciesResponse>> HandleAsync(
        ListInternalVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        var vacancies = await db.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == request.CompanyId
                && v.Status == VacancyStatus.Open
                && v.IsAdvertisedInternally)
            .Select(v => new { v.Id, v.PositionProfileId, v.AdvertTitle })
            .ToListAsync(cancellationToken);

        var positionProfileIds = vacancies
            .Select(v => v.PositionProfileId)
            .Distinct()
            .ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        var items = vacancies
            .Select(v =>
            {
                var positionProfile = positionProfilesById.GetValueOrDefault(v.PositionProfileId);

                return new InternalVacancyListItem(
                    v.Id,
                    v.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
                    positionProfile?.DepartmentName,
                    positionProfile?.LocationName,
                    null);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            items = items
                .Where(i =>
                    i.Title.Contains(request.Search, StringComparison.OrdinalIgnoreCase) ||
                    (i.DepartmentName?.Contains(request.Search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        items = items.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();

        return Result.Success(new ListInternalVacanciesResponse(items));
    }
}
