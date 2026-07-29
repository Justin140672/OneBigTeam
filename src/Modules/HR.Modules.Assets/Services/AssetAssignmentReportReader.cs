using HR.Modules.Assets.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Services;

internal sealed class AssetAssignmentReportReader(AssetsDbContext dbContext) : IAssetAssignmentReportReader
{
    public async Task<IReadOnlyList<AssetAssignmentReportItem>> GetAssetAssignmentsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from aa in dbContext.AssetAssignments.AsNoTracking()
            join a in dbContext.Assets.AsNoTracking() on aa.AssetId equals a.Id
            where aa.CompanyId == companyId
            select new AssetAssignmentReportItem(
                aa.Id,
                aa.EmployeeId,
                $"{a.AssetNumber} - {a.Name}",
                a.SerialNumber,
                aa.AssignedAt,
                aa.ReturnedAt == null ? "Assigned" : "Returned")
        ).ToListAsync(cancellationToken);

        return rows;
    }
}
