using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.ListSicknessCategories;

internal sealed class ListSicknessCategoriesHandler(SicknessDbContext db)
{
    public async Task<List<ListSicknessCategoriesResponse>> HandleAsync(
        ListSicknessCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.SicknessCategories
            .Where(c => c.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(c => c.IsActive);

        return await query
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new ListSicknessCategoriesResponse(
                c.Id, c.CompanyId, c.Name, c.IsActive, c.DisplayOrder, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
