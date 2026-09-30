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
        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId
                     && t.AssignedEmployeeId == null
                     && t.AssignedUserId == null
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            if (search.Length > 100) search = search[..100];
            var pattern = $"%{search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(t => EF.Functions.ILike(t.Title, pattern));
        }

        var items = await query
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
