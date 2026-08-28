using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetTask;

internal sealed class GetTaskHandler(TasksDbContext dbContext, TasksResourceAuthorizer resourceAuthorizer)
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

        // IAM-07: only the assignee, a manager anywhere in the assignee's reporting hierarchy,
        // or an HR Administrator may view this task. The specific assignee is only known after
        // the DB lookup above, so this check must live here rather than at the endpoint (mirrors
        // CompleteTaskHandler's identical resolution — see TasksResourceAuthorizer remarks).
        // Unassigned tasks have no self/hierarchy path; only the HR-administrator override
        // applies.
        var effectiveAssigneeId = task.AssignedEmployeeId ?? task.AssignedUserId;

        var isAuthorized = effectiveAssigneeId.HasValue
            ? await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                task.CompanyId, request.CallerEmployeeId, effectiveAssigneeId.Value, cancellationToken)
            : await resourceAuthorizer.IsHrAdministratorAsync(request.CallerEmployeeId, cancellationToken);

        if (!isAuthorized)
            return Result.Failure<GetTaskResponse>(
                Error.Forbidden("You are not authorized to view this task."));

        return Result.Success(new GetTaskResponse(
            task.Id,
            task.CompanyId,
            task.Title,
            task.Description,
            task.Status.ToString(),
            task.Priority.ToString(),
            task.Source.ToString(),
            task.ActionType.ToString(),
            task.DueDate,
            task.AssignedEmployeeId,
            task.AssignedUserId,
            task.SourceEntityId,
            task.CreatedBy,
            task.CompletedBy,
            task.CompletedAt,
            task.CreatedAt,
            task.UpdatedAt));
    }
}
