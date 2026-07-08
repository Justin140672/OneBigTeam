using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetUnassignedTasks;

internal sealed class GetUnassignedTasksHandler(TasksDbContext dbContext)
{
    public async Task<GetUnassignedTasksResponse> HandleAsync(
        GetUnassignedTasksRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId
                     && t.AssignedEmployeeId == null
                     && t.AssignedUserId == null
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .Take(200)
            .Select(t => new UnassignedTaskItem(
                t.Id,
                t.CompanyId,
                t.Title,
                t.Description,
                t.Status.ToString(),
                t.Priority.ToString(),
                t.Source.ToString(),
                t.ActionType.ToString(),
                t.DueDate,
                t.SourceEntityId,
                t.CreatedBy,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        return new GetUnassignedTasksResponse(items);
    }
}
