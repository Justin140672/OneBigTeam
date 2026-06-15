using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Features.CreateTask;

internal sealed class CreateTaskHandler(TasksDbContext dbContext, IClock clock)
{
    public async Task<Result<CreateTaskResponse>> HandleAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var task = TaskItem.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.CreatedBy,
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.Priority,
            request.Source,
            request.DueDate,
            request.AssignedEmployeeId,
            request.AssignedUserId,
            now);

        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateTaskResponse(
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
            task.CreatedAt,
            task.UpdatedAt));
    }
}
