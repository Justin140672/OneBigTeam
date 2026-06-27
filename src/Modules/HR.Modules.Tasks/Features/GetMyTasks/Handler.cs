using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed class GetMyTasksHandler(TasksDbContext dbContext, IEmployeeNameReader employeeNameReader)
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

        var raw = await query
            .OrderBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var nameMap = await employeeNameReader.GetNamesAsync(
            request.CompanyId,
            raw.Where(t => t.AssignedEmployeeId.HasValue).Select(t => t.AssignedEmployeeId!.Value),
            cancellationToken);

        var items = raw.Select(t => new TaskListItem(
            t.Id,
            t.CompanyId,
            t.Title,
            t.Description,
            t.Status.ToString(),
            t.Priority.ToString(),
            t.Source.ToString(),
            t.ActionType.ToString(),
            t.DueDate,
            t.AssignedEmployeeId,
            t.AssignedUserId,
            t.AssignedEmployeeId.HasValue ? nameMap.GetValueOrDefault(t.AssignedEmployeeId.Value) : null,
            t.CreatedBy,
            t.CompletedBy,
            t.CompletedAt,
            t.CreatedAt,
            t.UpdatedAt)).ToList();

        return new GetMyTasksResponse(items);
    }
}
