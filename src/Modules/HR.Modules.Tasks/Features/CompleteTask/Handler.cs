using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed class CompleteTaskHandler(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    TaskCompletionDispatcher dispatcher)
{
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

        if (task.Status == TaskItemStatus.Cancelled)
            return Result.Failure<CompleteTaskResponse>(
                Error.Conflict("Cannot complete a cancelled task."));

        var previousStatus = task.Status.ToString();

        task.Complete(request.CompletedBy, clock.UtcNowOffset());

        await dbContext.SaveChangesAsync(cancellationToken);

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
            task.CompletedAt!.Value), cancellationToken);

        await dispatcher.DispatchAsync(new TaskCompletionContext(
            task.CompanyId,
            task.Id,
            task.Title,
            task.Description,
            task.Source,
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
