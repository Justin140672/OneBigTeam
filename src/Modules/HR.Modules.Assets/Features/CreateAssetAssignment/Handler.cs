using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAssetAssignment;

internal sealed class CreateAssetAssignmentHandler(AssetsDbContext db, IClock clock, ITaskCreator taskCreator)
{
    public async Task<Result<CreateAssetAssignmentResponse>> HandleAsync(
        CreateAssetAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(
            a => a.Id == request.AssetId && a.CompanyId == request.CompanyId,
            cancellationToken);

        if (asset is null)
            return Result.Failure<CreateAssetAssignmentResponse>(
                Error.NotFound("Asset not found."));

        if (asset.Status != AssetStatus.Available)
            return Result.Failure<CreateAssetAssignmentResponse>(
                Error.Conflict($"Asset is not available for assignment (current status: {asset.Status})."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var assignment = AssetAssignment.Create(
            Guid.NewGuid(), request.CompanyId, request.AssetId,
            request.EmployeeId, request.AssignedBy, request.Notes, now);

        asset.MarkAssigned(now);

        db.AssetAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          request.AssignedBy,
            title:              $"Acknowledge receipt of asset",
            description:        $"Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Acknowledge,
            dueDate:            DateOnly.FromDateTime(clock.UtcNow.AddDays(7)),
            assignedEmployeeId: request.EmployeeId,
            assignedUserId:     null,
            sourceEntityId:     assignment.Id,
            cancellationToken);

        return Result.Success(new CreateAssetAssignmentResponse(
            assignment.Id, assignment.CompanyId, assignment.AssetId,
            assignment.EmployeeId, assignment.AssignedBy,
            assignment.AssignedAt, assignment.Notes, assignment.CreatedAt));
    }
}
