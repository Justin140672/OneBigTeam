using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.RequestAssetReturn;

internal sealed class RequestAssetReturnHandler(
    AssetsDbContext db,
    ITaskCreator taskCreator,
    IOpenTaskBySourceEntityReader openTaskReader,
    IClock clock,
    INotificationWriter notificationWriter,
    IAuditEventPublisher auditPublisher)
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

        var now = clock.UtcNowOffset();

        // AssetTaskCompletionAction already creates a "Return asset" task automatically the
        // moment the employee acknowledges receiving the asset (by design — see that class's own
        // tests) — this explicit admin-initiated request is a SEPARATE trigger for the same kind
        // of task against the same assignment. Without this check, both paths create their own
        // "Return asset" task, leaving two open tasks for one assignment.
        var existingReturnTaskId = await openTaskReader.GetOpenTaskIdForAssigneeAsync(
            request.CompanyId, assignment.Id, assignment.EmployeeId, TaskActionType.Return, cancellationToken);

        if (existingReturnTaskId is null)
        {
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
                cancellationToken,
                notifyAssignee:     false);
        }

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            request.CompanyId,
            assignment.EmployeeId,
            "Asset return requested",
            "You have been asked to return an assigned asset.",
            assignment.Id,
            NotificationType.AssetReturnRequested,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new AssetReturnRequestedAuditEvent(
            request.CompanyId,
            assignment.Id,
            assignment.EmployeeId,
            request.RequestedBy,
            now), cancellationToken);

        return Result.Success();
    }
}
