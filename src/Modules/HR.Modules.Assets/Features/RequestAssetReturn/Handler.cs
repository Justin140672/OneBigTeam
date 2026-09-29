using HR.Modules.Tasks.Contracts;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;
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
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, object?>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var assignment = await db.AssetAssignments
            .FirstOrDefaultAsync(
                a => a.Id == request.Id && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (assignment is null)
            return Result.Failure(Error.NotFound("Asset assignment not found."));

        if (!assignment.IsActive)
            return Result.Failure(Error.Conflict("Asset has already been returned."));

        var now = clock.UtcNowOffset();

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, object?>(db.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status204NoContent, null, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }

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
