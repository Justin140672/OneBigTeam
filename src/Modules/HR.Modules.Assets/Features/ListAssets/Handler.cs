using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListAssets;

internal sealed class ListAssetsHandler(AssetsDbContext db)
{
    public async Task<List<ListAssetsResponse>> HandleAsync(
        ListAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.Assets
            .Where(a => a.CompanyId == request.CompanyId);

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        var assets = await query
            .OrderBy(a => a.AssetNumber)
            .ToListAsync(cancellationToken);

        if (assets.Count == 0)
            return [];

        // Category names are resolved as a separate, small lookup query and merged client-side —
        // AssetCategories per company is a bounded, small list, so this avoids relying on a
        // GroupJoin/SelectMany "left join" LINQ shape translating correctly through the Npgsql EF
        // Core provider (it previously did not — every ListAssets call failed with a 500 once that
        // shape was introduced, confirmed via this endpoint's own integration tests). An asset must
        // never silently disappear from the list just because its category row can't be resolved
        // (e.g. a stale/orphaned CategoryId) — it still shows up, with a fallback category label,
        // rather than vanishing, exactly as the removed left join intended.
        var categoryIds = assets.Select(a => a.CategoryId).Distinct().ToList();
        var categoryNames = await db.AssetCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return assets
            .Select(a => new ListAssetsResponse(
                a.Id,
                a.CompanyId,
                a.AssetNumber,
                a.CategoryId,
                categoryNames.TryGetValue(a.CategoryId, out var name) ? name : "Unknown",
                a.Name,
                a.Manufacturer,
                a.Model,
                a.SerialNumber,
                a.PurchaseDate,
                a.PurchasePrice,
                a.Status,
                a.CreatedAt,
                a.UpdatedAt))
            .ToList();
    }
}
