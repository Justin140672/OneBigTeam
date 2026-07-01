using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListAssetAssignments;

internal sealed class ListAssetAssignmentsHandler(AssetsDbContext db)
{
    public async Task<List<ListAssetAssignmentsResponse>> HandleAsync(
        ListAssetAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        var assignments = await db.AssetAssignments
            .Where(aa => aa.CompanyId == request.CompanyId && aa.AssetId == request.AssetId)
            .OrderByDescending(aa => aa.AssignedAt)
            .ToListAsync(cancellationToken);

        return assignments
            .Select(aa => new ListAssetAssignmentsResponse(
                aa.Id,
                aa.CompanyId,
                aa.AssetId,
                aa.EmployeeId,
                aa.AssignedBy,
                aa.AssignedAt,
                aa.AcknowledgedAt,
                aa.ReturnedAt,
                aa.Notes,
                aa.CreatedAt,
                aa.UpdatedAt,
                aa.IsActive))
            .ToList();
    }
}
