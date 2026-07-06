using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;

internal sealed class ListRequiredAssetsHandler(
    EmployeesDbContext dbContext,
    IAssetCategoryReader assetCategoryReader)
{
    public async Task<Result<ListRequiredAssetsResponse>> HandleAsync(
        ListRequiredAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<ListRequiredAssetsResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var rows = await dbContext.PositionProfileRequiredAssets
            .AsNoTracking()
            .Where(a => a.PositionProfileId == request.PositionProfileId
                     && a.CompanyId == request.CompanyId
                     && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.AssetCategoryId, a.IsMandatory, a.Quantity })
            .ToListAsync(cancellationToken);

        var names = await assetCategoryReader.GetNamesAsync(
            request.CompanyId,
            rows.Select(r => r.AssetCategoryId),
            cancellationToken);

        var items = rows.Select(r => new RequiredAssetListItem(
            r.Id,
            r.AssetCategoryId,
            names.GetValueOrDefault(r.AssetCategoryId, string.Empty),
            r.IsMandatory,
            r.Quantity)).ToList();

        return Result.Success(new ListRequiredAssetsResponse(items));
    }
}
