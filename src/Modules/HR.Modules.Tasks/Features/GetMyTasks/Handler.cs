using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed class GetMyTasksHandler(TasksDbContext dbContext)
{
    public async Task<GetMyTasksResponse> HandleAsync(
        GetMyTasksRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId && t.AssignedUserId == request.UserId);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TaskItemStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(t => t.Status == status);
        }

        var items = await query
            .OrderBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new TaskListItem(
                t.Id,
                t.CompanyId,
                t.Title,
                t.Description,
                t.Status.ToString(),
                t.Priority.ToString(),
                t.Source.ToString(),
                t.DueDate,
                t.AssignedEmployeeId,
                t.AssignedUserId,
                t.CreatedBy,
                t.CompletedBy,
                t.CompletedAt,
                t.CreatedAt,
                t.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new GetMyTasksResponse(items);
    }
}
