using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListEmployeeAssets;

internal sealed class ListEmployeeAssetsHandler(AssetsDbContext db, AssetResourceAuthorizer authorizer)
{
    public async Task<Result<List<ListEmployeeAssetsResponse>>> HandleAsync(
        ListEmployeeAssetsRequest request,
        Guid? callerUserId,
        CancellationToken cancellationToken)
    {
        if (callerUserId is not { } callerId
            || !await authorizer.CanViewEmployeeAssetsAsync(
                request.CompanyId, callerId, request.EmployeeId, cancellationToken))
            return Result.Failure<List<ListEmployeeAssetsResponse>>(
                Error.Forbidden("You do not have permission to view these assets."));

        var assignments = await db.AssetAssignments
            .AsNoTracking()
            .Where(aa => aa.CompanyId == request.CompanyId
                      && aa.EmployeeId == request.EmployeeId
                      && aa.ReturnedAt == null)
            .OrderByDescending(aa => aa.AssignedAt)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
            return Result.Success(new List<ListEmployeeAssetsResponse>());

        var assetIds = assignments.Select(aa => aa.AssetId).ToList();

        var assets = await db.Assets
            .AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var categoryIds = assets.Values.Select(a => a.CategoryId).Distinct().ToList();
        var categories = await db.AssetCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var result = assignments
            .Where(aa => assets.ContainsKey(aa.AssetId))
            .Select(aa =>
            {
                var asset = assets[aa.AssetId];
                categories.TryGetValue(asset.CategoryId, out var categoryName);
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
                    asset.SerialNumber,
                    categoryName,
                    aa.AcknowledgedAt is not null);
            })
            .ToList();

        return Result.Success(result);
    }
}
