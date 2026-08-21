using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed class ListVacanciesHandler(RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
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

        if (request.ExcludeClosed)
            query = query.Where(v => v.Status != Domain.VacancyStatus.Closed);

        if (request.PositionProfileId.HasValue)
            query = query.Where(v => v.PositionProfileId == request.PositionProfileId.Value);

        if (request.DepartmentId.HasValue)
        {
            // Vacancy has no department column of its own — resolve the matching Position Profile IDs
            // first via the narrow IPositionProfileReader contract, then filter by that set.
            var positionProfileIdsInDepartment = (await positionProfileReader.GetIdsByDepartmentAsync(
                request.CompanyId, request.DepartmentId.Value, cancellationToken)).ToList();

            query = query.Where(v => positionProfileIdsInDepartment.Contains(v.PositionProfileId));
        }

        var orderedQuery = query.OrderByDescending(v => v.CreatedAt).AsQueryable();

        // Bound the expensive per-row enrichment below (a batched, but still per-vacancy-scaling,
        // Position Profile summary lookup plus a GroupBy application-count query) at the SQL level
        // when there's no search text to honour — the common "just open the dropdown" case. When
        // Search is also supplied, defer bounding until after the in-memory search filter further
        // down instead (see there), since Search itself only runs in-memory over whatever this
        // query already fetched — bounding here first could silently exclude the very vacancy the
        // caller is typing to find.
        if (request.PageSize is > 0 && string.IsNullOrWhiteSpace(request.Search))
            orderedQuery = orderedQuery.Take(request.PageSize.Value);

        var vacancies = await orderedQuery
            .Select(v => new
            {
                v.Id,
                v.PositionProfileId,
                v.AdvertTitle,
                v.Status,
                v.HiringManagerId,
                v.AssignedRecruiterId,
                v.OpenedAt,
                v.ClosedAt,
                v.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        // Batch cross-module read: resolves every linked Position Profile's canonical title/department
        // in one round trip (via the narrow IPositionProfileReader contract) rather than N+1 queries.
        var positionProfileIds = vacancies
            .Select(v => v.PositionProfileId)
            .Distinct()
            .ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        var vacancyIds = vacancies.Select(v => v.Id).ToList();

        var applicationCounts = vacancyIds.Count > 0
            ? await db.Applications
                .AsNoTracking()
                .Where(a => a.CompanyId == request.CompanyId && vacancyIds.Contains(a.VacancyId))
                .GroupBy(a => a.VacancyId)
                .Select(g => new { VacancyId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.VacancyId, x => x.Count, cancellationToken)
            : new Dictionary<Guid, int>();

        var items = vacancies
            .Select(v =>
            {
                var positionProfile = positionProfilesById.GetValueOrDefault(v.PositionProfileId);

                return new VacancyListItem(
                    v.Id,
                    v.PositionProfileId,
                    v.AdvertTitle,
                    v.Status,
                    v.HiringManagerId,
                    v.AssignedRecruiterId,
                    v.OpenedAt,
                    v.ClosedAt,
                    v.CreatedAt,
                    positionProfile?.Title,
                    positionProfile?.DepartmentId,
                    v.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
                    positionProfile?.LocationName,
                    applicationCounts.GetValueOrDefault(v.Id));
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            items = items
                .Where(i =>
                    i.EffectiveTitle.Contains(request.Search, StringComparison.OrdinalIgnoreCase) ||
                    (i.PositionProfileTitle?.Contains(request.Search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            if (request.PageSize is > 0)
                items = items.Take(request.PageSize.Value).ToList();
        }

        return Result.Success(new ListVacanciesResponse(items));
    }
}
