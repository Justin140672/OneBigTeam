using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed class GetTeamTasksHandler(
    TasksDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<GetTeamTasksResponse> HandleAsync(
        GetTeamTasksRequest request,
        CancellationToken cancellationToken)
    {
        var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
            request.CompanyId,
            request.ManagerId,
            cancellationToken);

        if (directReportIds.Count == 0)
            return new GetTeamTasksResponse([]);

        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId
                     && t.AssignedEmployeeId != null
                     && directReportIds.Contains(t.AssignedEmployeeId.Value));

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TaskItemStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(t => t.Status == status);
        }

        // Capped rather than unbounded: without a status filter this can otherwise grow forever
        // as completed/cancelled history accumulates. Terminal-status tasks sort last so the cap
        // only ever trims old history, never a currently open/in-progress task.
        var raw = await query
            .OrderBy(t => t.Status == TaskItemStatus.Completed || t.Status == TaskItemStatus.Cancelled ? 1 : 0)
            .ThenBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var nameMap = await employeeNameReader.GetNamesAsync(
            request.CompanyId,
            raw.Where(t => t.AssignedEmployeeId.HasValue).Select(t => t.AssignedEmployeeId!.Value),
            cancellationToken);

        var items = raw.Select(t => new TeamTaskItem(
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

        return new GetTeamTasksResponse(items);
    }
}
