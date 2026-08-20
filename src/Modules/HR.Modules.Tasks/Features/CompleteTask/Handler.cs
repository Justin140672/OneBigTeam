using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed class CompleteTaskHandler(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    TaskCompletionDispatcher dispatcher,
    IAuthorizationService authorizationService,
    IDirectReportsReader directReportsReader)
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Tasks cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as GetRecentLeaveRequests/Endpoint.cs's
    // HrAdministratorRoleId and GetTeamSicknessToday's SicknessManagePermissionId.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    public async Task<Result<CompleteTaskResponse>> HandleAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (task is null)
            return Result.Failure<CompleteTaskResponse>(
                Error.NotFound($"Task with id '{request.Id}' was not found."));

        // SEC-003: only the assignee, the assignee's manager (anywhere in the reporting
        // hierarchy), or an HR Administrator may complete a task. Endpoint-level
        // Policies("role:employee") only proves tenant membership, not resource ownership —
        // the task's specific assignee is only known after this DB lookup, so the check must
        // live here rather than at the endpoint. This runs before the cancelled-status check
        // and before task.Complete()'s already-completed short-circuit, so an unauthorized
        // caller can never use either as a bypass.
        var isAssignee = task.AssignedUserId == request.CompletedBy || task.AssignedEmployeeId == request.CompletedBy;

        var isHrAdministrator = (await authorizationService.GetEffectiveRolesAsync(request.CompletedBy, cancellationToken))
            .Contains(HrAdministratorRoleId);

        // Employee ID and User ID are the same value by construction throughout this app (see
        // GetMyTasksHandler.cs), so AssignedUserId is a valid fallback when AssignedEmployeeId
        // is null — a task assigned only via AssignedUserId must still be reachable by that
        // person's manager.
        var effectiveAssigneeId = task.AssignedEmployeeId ?? task.AssignedUserId;

        var isManagerInHierarchy = false;
        if (effectiveAssigneeId.HasValue)
        {
            var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
                task.CompanyId, request.CompletedBy, cancellationToken);
            isManagerInHierarchy = descendantIds.Contains(effectiveAssigneeId.Value);
        }

        // Unassigned tasks (AssignedEmployeeId and AssignedUserId both null) have no
        // assignee/manager path — only the HR override can complete them.
        if (!isAssignee && !isHrAdministrator && !isManagerInHierarchy)
            return Result.Failure<CompleteTaskResponse>(
                Error.Forbidden("You are not authorized to complete this task."));

        if (task.Status == TaskItemStatus.Cancelled)
            return Result.Failure<CompleteTaskResponse>(
                Error.Conflict("Cannot complete a cancelled task."));

        var previousStatus = task.Status.ToString();

        // Idempotency: TaskItem.Complete() is itself a no-op when the task is already
        // Completed (see TaskItem.cs), but the handler was previously writing a fresh
        // notification/audit event/dispatch on every call regardless — a second completion
        // request for an already-completed task would violate the notifications table's
        // (employee_id, source_entity_id, type) uniqueness constraint and 500, plus fire
        // duplicate audit events and downstream actions (e.g. leave/probation/asset
        // completion side effects) a second time. Side effects must only fire on the actual
        // Open/InProgress -> Completed transition, not on a repeat call.
        var wasAlreadyCompleted = task.Status == TaskItemStatus.Completed;

        task.Complete(request.CompletedBy, clock.UtcNowOffset());

        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasAlreadyCompleted)
        {
            return Result.Success(new CompleteTaskResponse(
                task.Id,
                task.CompanyId,
                task.Title,
                task.Description,
                task.Status.ToString(),
                task.Priority.ToString(),
                task.Source.ToString(),
                task.DueDate,
                task.AssignedEmployeeId,
                task.AssignedUserId,
                task.CreatedBy,
                task.CompletedBy,
                task.CompletedAt,
                task.CreatedAt,
                task.UpdatedAt));
        }

        if (task.AssignedEmployeeId.HasValue)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), task.CompanyId, task.AssignedEmployeeId.Value,
                $"Task completed: {task.Title}",
                null,
                task.Id,
                NotificationType.TaskCompleted,
                NotificationPriority.Normal,
                clock.UtcNowOffset(),
                cancellationToken);
        }

        await auditPublisher.PublishAsync(new TaskCompletedAuditEvent(
            task.CompanyId,
            task.Id,
            task.CompletedBy!.Value,
            previousStatus,
            task.AssignedEmployeeId,
            task.CompletedAt!.Value), cancellationToken);

        await dispatcher.DispatchAsync(new TaskCompletionContext(
            task.CompanyId,
            task.Id,
            task.Title,
            task.Description,
            task.Source,
            task.ActionType,
            task.AssignedEmployeeId,
            task.CompletedBy!.Value,
            task.CompletedAt!.Value,
            task.SourceEntityId,
            request.OutcomeDecision,
            request.OutcomeReason), cancellationToken);

        return Result.Success(new CompleteTaskResponse(
            task.Id,
            task.CompanyId,
            task.Title,
            task.Description,
            task.Status.ToString(),
            task.Priority.ToString(),
            task.Source.ToString(),
            task.DueDate,
            task.AssignedEmployeeId,
            task.AssignedUserId,
            task.CreatedBy,
            task.CompletedBy,
            task.CompletedAt,
            task.CreatedAt,
            task.UpdatedAt));
    }
}
