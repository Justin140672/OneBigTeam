using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed class UpdateSupportRequestStatusHandler(
    SupportDbContext db,
    IClock clock,
    IHrAdministratorDirectory hrAdministratorDirectory,
    INotificationWriter notificationWriter)
{
    public async Task<Result<UpdateSupportRequestStatusResponse>> HandleAsync(
        UpdateSupportRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.SupportRequests
            .SingleOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure<UpdateSupportRequestStatusResponse>(Error.NotFound("Support request not found."));

        if (!entity.CanTransitionTo(request.Status))
            return Result.Failure<UpdateSupportRequestStatusResponse>(
                Error.Conflict("A closed support request cannot be reopened directly back to Submitted."));

        var previousStatus = entity.Status;
        var now = clock.UtcNowOffset();
        entity.ChangeStatus(request.Status, now);
        await db.SaveChangesAsync(cancellationToken);

        // Notify every HR Administrator for this company whenever a ticket's status actually
        // changes — mirrors how other modules (Tasks, Offboarding, Sickness, ...) fan out an
        // in-app notification via the same shared INotificationWriter. Best-effort: a notification
        // failure must never fail the status change itself, which has already been saved.
        //
        // The notifications table enforces (employee_id, source_entity_id, type) uniqueness — see
        // NotificationConfiguration.cs — because most notification types fire at most once per
        // entity (e.g. TaskAssigned). A support request can change status many times over its
        // lifecycle, so a second/third status change would otherwise violate that constraint and
        // 500 the whole request. RemoveBySourceEntityAsync-then-write is the established pattern
        // for this (see TaskCompleter.cs, UpdateSharedCompanyDocumentAcknowledgementSettings) —
        // it collapses to a single live notification per HR admin reflecting the latest status,
        // re-surfaced as unread on every change, rather than accumulating one row per transition.
        if (previousStatus != entity.Status)
        {
            var hrAdminEmployeeIds = await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(
                request.CompanyId, cancellationToken);

            await notificationWriter.RemoveBySourceEntityAsync(
                request.CompanyId, entity.Id, NotificationType.SupportRequestStatusChanged, cancellationToken);

            foreach (var employeeId in hrAdminEmployeeIds)
            {
                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    request.CompanyId,
                    employeeId,
                    $"Support request '{entity.ReferenceNumber}' status changed to {entity.Status}",
                    entity.Title,
                    entity.Id,
                    NotificationType.SupportRequestStatusChanged,
                    NotificationPriority.Normal,
                    now,
                    cancellationToken);
            }
        }

        return Result.Success(new UpdateSupportRequestStatusResponse(entity.Id, entity.Status.ToString(), entity.UpdatedAt));
    }
}
