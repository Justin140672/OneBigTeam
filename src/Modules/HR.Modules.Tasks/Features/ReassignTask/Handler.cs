using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed class ReassignTaskHandler(TasksDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
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

        var previousEmployeeId = task.AssignedEmployeeId;
        var previousUserId     = task.AssignedUserId;

        task.Reassign(request.AssignedEmployeeId, request.AssignedUserId, clock.UtcNowOffset());

        await dbContext.SaveChangesAsync(cancellationToken);

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
}
