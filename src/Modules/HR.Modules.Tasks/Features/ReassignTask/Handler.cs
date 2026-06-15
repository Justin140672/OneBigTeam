using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed class ReassignTaskHandler(TasksDbContext dbContext, IClock clock)
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

        task.Reassign(request.AssignedEmployeeId, request.AssignedUserId, clock.UtcNowOffset());

        await dbContext.SaveChangesAsync(cancellationToken);

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
