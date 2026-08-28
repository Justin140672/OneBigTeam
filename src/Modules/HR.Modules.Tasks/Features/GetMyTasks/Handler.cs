using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed class GetMyTasksHandler(TasksDbContext dbContext, IEmployeeNameReader employeeNameReader)
{
    public async Task<GetMyTasksResponse> HandleAsync(
        GetMyTasksRequest request,
        CancellationToken cancellationToken)
    {
        // AssignedUserId alone misses tasks created before the assignee had a linked user account
        // (several task-creation call sites — RequestAdditionalEmployeeDocument,
        // EmployeeCreatedHandler, ProcessDocumentExpiryNotifications, UploadMyProfilePhoto, asset
        // assignment — pass assignedUserId: null unconditionally, relying on AssignedEmployeeId
        // alone). Employee ID and User ID are the same value by construction throughout this app
        // (see SignUpHandler/AcceptInvite/EnsureDevSupabaseUserAsync's own remarks on this
        // invariant), so matching AssignedEmployeeId against the caller's UserId here correctly
        // picks those tasks up too, without needing to fix every writer individually.
        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId
                && (t.AssignedUserId == request.UserId || t.AssignedEmployeeId == request.UserId));

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TaskItemStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(request.Priority) &&
            Enum.TryParse<TaskPriority>(request.Priority, ignoreCase: true, out var priority))
        {
            query = query.Where(t => t.Priority == priority);
        }

        if (request.DueDateFrom is not null)
            query = query.Where(t => t.DueDate != null && t.DueDate >= request.DueDateFrom);

        if (request.DueDateTo is not null)
            query = query.Where(t => t.DueDate != null && t.DueDate <= request.DueDateTo);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize   = request.PageSize   < 1 ? 20 : request.PageSize;

        var raw = await query
            .OrderBy(t => t.Status == TaskItemStatus.Completed || t.Status == TaskItemStatus.Cancelled ? 1 : 0)
            .ThenBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
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

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return new GetMyTasksResponse(items, totalCount, pageNumber, pageSize, totalPages);
    }
}
