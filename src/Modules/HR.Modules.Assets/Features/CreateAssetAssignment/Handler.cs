using HR.Modules.Tasks.Contracts;
using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAssetAssignment;

internal sealed class CreateAssetAssignmentHandler(AssetsDbContext db, IClock clock, ITaskCreator taskCreator, INotificationWriter notificationWriter, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateAssetAssignmentResponse>> HandleAsync(
        CreateAssetAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateAssetAssignmentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateAssetAssignmentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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

        var response = new CreateAssetAssignmentResponse(
            assignment.Id, assignment.CompanyId, assignment.AssetId,
            assignment.EmployeeId, assignment.AssignedBy,
            assignment.AssignedAt, assignment.Notes, assignment.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new AssetAssignedAuditEvent(
            assignment.CompanyId,
            assignment.Id,
            assignment.AssetId,
            assignment.EmployeeId,
            assignment.AssignedBy,
            now), cancellationToken);

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
            cancellationToken,
            notifyAssignee:     false);

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            "Asset assigned to you",
            $"The asset \"{asset.Name}\" has been assigned to you.",
            assignment.Id,
            NotificationType.AssetAssigned,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        return Result.Success(response);
    }
}
