using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed class GetTeamTasksHandler(TasksDbContext dbContext, IDirectReportsReader directReportsReader)
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

        var items = await query
            .OrderBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new TeamTaskItem(
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

        return new GetTeamTasksResponse(items);
    }
}
