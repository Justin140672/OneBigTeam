using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed class ReassignTaskHandler(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    TasksResourceAuthorizer resourceAuthorizer)
{
    public async Task<Result<ReassignTaskResponse>> HandleAsync(
        ReassignTaskRequest request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (task is null)
            return Result.Failure<ReassignTaskResponse>(
                Error.NotFound($"Task with id '{request.Id}' was not found."));

        // DSH-01: reassignment is a task-resource operation and must apply the same self /
        // manager-hierarchy / HR-administrator rule as viewing and completing a task (see
        // GetTaskHandler / CompleteTaskHandler). The endpoint's "employee:manage" policy only
        // proves the caller may manage employees in general — not that they have any relationship
        // to *this* task's current assignee, whose identity is only known after the lookup above.
        // The check runs against the current assignee, before any mutation. Unassigned tasks have
        // no self/hierarchy path, so only the HR-administrator override applies.
        // request.ActorUserId is always populated by the endpoint from ICurrentUser; the fallback
        // keeps pre-DSH-01 handler unit tests compiling and denies (Guid.Empty is nobody's
        // manager) in every real path where it is somehow absent.
        var actorUserId = request.ActorUserId ?? Guid.Empty;
        var effectiveAssigneeId = task.AssignedEmployeeId ?? task.AssignedUserId;

        var isAuthorized = effectiveAssigneeId.HasValue
            ? await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                task.CompanyId, actorUserId, effectiveAssigneeId.Value, cancellationToken)
            : await resourceAuthorizer.IsHrAdministratorAsync(actorUserId, cancellationToken);

        if (!isAuthorized)
            return Result.Failure<ReassignTaskResponse>(
                Error.Forbidden("You are not authorized to reassign this task."));

        var previousEmployeeId = task.AssignedEmployeeId;
        var previousUserId     = task.AssignedUserId;

        task.Reassign(request.AssignedEmployeeId, request.AssignedUserId, clock.UtcNowOffset());

        await dbContext.SaveChangesAsync(cancellationToken);

        var newEmployeeId = task.AssignedEmployeeId;
        if (newEmployeeId.HasValue && newEmployeeId != previousEmployeeId)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), task.CompanyId, newEmployeeId.Value,
                $"New task assigned: {task.Title}",
                task.Description,
                task.Id,
                NotificationType.TaskAssigned,
                ToNotificationPriority(task.Priority),
                clock.UtcNowOffset(),
                cancellationToken);
        }

        await auditPublisher.PublishAsync(new TaskReassignedAuditEvent(
            task.CompanyId,
            task.Id,
            request.ActorUserId,
            previousEmployeeId,
            previousUserId,
            task.AssignedEmployeeId,
            task.AssignedUserId,
            task.UpdatedAt), cancellationToken);

        return Result.Success(new ReassignTaskResponse(
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

    private static NotificationPriority ToNotificationPriority(TaskPriority priority) => priority switch
    {
        TaskPriority.Critical => NotificationPriority.Urgent,
        TaskPriority.High     => NotificationPriority.High,
        TaskPriority.Medium   => NotificationPriority.Normal,
        _                     => NotificationPriority.Low,
    };
}
