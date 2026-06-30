using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.GetAssetAssignment;

internal sealed class GetAssetAssignmentHandler(AssetsDbContext db)
{
    public async Task<Result<GetAssetAssignmentResponse>> HandleAsync(
        GetAssetAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var assignment = await db.AssetAssignments
            .FirstOrDefaultAsync(
                a => a.Id == request.Id
                     && a.AssetId == request.AssetId
                     && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (assignment is null)
            return Result.Failure<GetAssetAssignmentResponse>(Error.NotFound("Asset assignment not found."));

        return Result.Success(new GetAssetAssignmentResponse(
            assignment.Id,
            assignment.CompanyId,
            assignment.AssetId,
            assignment.EmployeeId,
            assignment.AssignedBy,
            assignment.AssignedAt,
            assignment.ReturnedAt,
            assignment.Notes,
            assignment.CreatedAt,
            assignment.UpdatedAt,
            assignment.IsActive));
    }
}
