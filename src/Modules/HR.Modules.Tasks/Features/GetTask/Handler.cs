using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetTask;

internal sealed class GetTaskHandler(TasksDbContext dbContext)
{
    public async Task<Result<GetTaskResponse>> HandleAsync(
        GetTaskRequest request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (task is null)
            return Result.Failure<GetTaskResponse>(
                Error.NotFound($"Task '{request.Id}' was not found."));

        return Result.Success(new GetTaskResponse(
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
