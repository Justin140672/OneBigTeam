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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>>> GetAssignedAssetsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<AssignedAssetItem>>();

        var rows = await (
            from aa in dbContext.AssetAssignments.AsNoTracking()
            join a in dbContext.Assets.AsNoTracking() on aa.AssetId equals a.Id
            join c in dbContext.AssetCategories.AsNoTracking() on a.CategoryId equals c.Id into categoryJoin
            from c in categoryJoin.DefaultIfEmpty()
            where aa.CompanyId == companyId
               && employeeIds.Contains(aa.EmployeeId)
               && aa.ReturnedAt == null
            select new
            {
                aa.EmployeeId,
                Item = new AssignedAssetItem(
                    aa.Id,
                    aa.AssetId,
                    c != null ? $"{a.AssetNumber} - {a.Name} ({c.Name})" : $"{a.AssetNumber} - {a.Name}"),
            }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<AssignedAssetItem> (g) => g.Select(r => r.Item).ToList());
    }
}
