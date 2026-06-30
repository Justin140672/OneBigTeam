using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListEmployeeAssets;

internal sealed class ListEmployeeAssetsHandler(AssetsDbContext db)
{
    public async Task<List<ListEmployeeAssetsResponse>> HandleAsync(
        ListEmployeeAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var assignments = await db.AssetAssignments
            .Where(aa => aa.CompanyId == request.CompanyId
                      && aa.EmployeeId == request.EmployeeId
                      && aa.ReturnedAt == null)
            .OrderByDescending(aa => aa.AssignedAt)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
            return [];

        var assetIds = assignments.Select(aa => aa.AssetId).ToList();

        var assets = await db.Assets
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        return assignments
            .Where(aa => assets.ContainsKey(aa.AssetId))
            .Select(aa =>
            {
                var asset = assets[aa.AssetId];
                return new ListEmployeeAssetsResponse(
                    aa.Id,
                    aa.AssetId,
                    aa.EmployeeId,
                    aa.AssignedBy,
                    aa.AssignedAt,
                    aa.Notes,
                    asset.AssetNumber,
                    asset.Name,
                    asset.Manufacturer,
                    asset.Model,
                    asset.SerialNumber);
            })
            .ToList();
    }
}
