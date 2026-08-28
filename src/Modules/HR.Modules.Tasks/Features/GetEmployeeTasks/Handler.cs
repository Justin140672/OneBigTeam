using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed class GetEmployeeTasksHandler(TasksDbContext dbContext, IEmployeeNameReader employeeNameReader)
{
    public async Task<GetEmployeeTasksResponse> HandleAsync(
        GetEmployeeTasksRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId && t.AssignedEmployeeId == request.EmployeeId);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TaskItemStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(t => t.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize   = request.PageSize   < 1 ? 20 : request.PageSize;

        var raw = await query
            .OrderBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var nameMap = await employeeNameReader.GetNamesAsync(
            request.CompanyId,
            raw.Where(t => t.AssignedEmployeeId.HasValue).Select(t => t.AssignedEmployeeId!.Value),
            cancellationToken);

        var items = raw.Select(t => new EmployeeTaskItem(
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
            t.UpdatedAt,
            t.SourceEntityId)).ToList();

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return new GetEmployeeTasksResponse(items, totalCount, pageNumber, pageSize, totalPages);
    }
}
