using HR.Modules.Assets.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Services;

internal sealed class AssignedAssetReader(AssetsDbContext dbContext) : IAssignedAssetReader
{
    public async Task<IReadOnlyList<AssignedAssetItem>> GetAssignedAssetsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from aa in dbContext.AssetAssignments.AsNoTracking()
            join a in dbContext.Assets.AsNoTracking() on aa.AssetId equals a.Id
            join c in dbContext.AssetCategories.AsNoTracking() on a.CategoryId equals c.Id into categoryJoin
            from c in categoryJoin.DefaultIfEmpty()
            where aa.CompanyId == companyId
               && aa.EmployeeId == employeeId
               && aa.ReturnedAt == null
            select new AssignedAssetItem(
                aa.Id,
                aa.AssetId,
                c != null ? $"{a.AssetNumber} - {a.Name} ({c.Name})" : $"{a.AssetNumber} - {a.Name}")
        ).ToListAsync(cancellationToken);

        return rows;
    }
}
