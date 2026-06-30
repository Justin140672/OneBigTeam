using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.RequestAssetReturn;

internal sealed class RequestAssetReturnHandler(AssetsDbContext db, ITaskCreator taskCreator, IClock clock)
{
    public async Task<Result> HandleAsync(
        RequestAssetReturnRequest request,
        CancellationToken cancellationToken)
    {
        var assignment = await db.AssetAssignments
            .FirstOrDefaultAsync(
                a => a.Id == request.Id && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (assignment is null)
            return Result.Failure(Error.NotFound("Asset assignment not found."));

        if (!assignment.IsActive)
            return Result.Failure(Error.Conflict("Asset has already been returned."));

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          request.RequestedBy,
            title:              "Return asset",
            description:        "Please return the assigned asset.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Return,
            dueDate:            DateOnly.FromDateTime(clock.UtcNow.AddDays(7)),
            assignedEmployeeId: assignment.EmployeeId,
            assignedUserId:     null,
            sourceEntityId:     assignment.Id,
            cancellationToken);

        return Result.Success();
    }
}
