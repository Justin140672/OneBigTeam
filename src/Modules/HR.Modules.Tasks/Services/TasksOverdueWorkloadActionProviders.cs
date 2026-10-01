using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class EmployeeTasksOverdueWorkloadActionProvider(
    TasksDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Employee Tasks Overdue";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
            return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var overdue = await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.AssignedEmployeeId == callerEmployeeId
                     && t.DueDate != null && t.DueDate < today
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress))
            .Select(t => new { t.Id, t.Title, t.DueDate, t.AssignedEmployeeId })
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, [callerEmployeeId], cancellationToken);
        departments.TryGetValue(callerEmployeeId, out var dept);

        return overdue.Select(t => new WorkloadAction(
            EmployeeId: callerEmployeeId,
            EmployeeName: dept?.EmployeeName ?? callerEmployeeId.ToString(),
            Department: dept?.DepartmentName,
            ActionType: t.Title,
            ActionCategory: ActionCategory,
            DueDate: t.DueDate,
            AssignedTo: dept?.EmployeeName,
            Status: "Overdue",
            DeepLinkUrl: $"/companies/{companyId}/tasks/{t.Id}",
            TaskId: t.Id)).ToList();
    }
}

internal sealed class ManagerTasksOverdueWorkloadActionProvider(
    TasksDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Manager Tasks Overdue";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? employeeIds = null;
        var hrScope = requestedScope == WorkloadScope.Hr;
        if (hrScope)
        {
            var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
            if (!callerIsHr)
                return [];
        }
        else
        {
            if (currentUser.UserId is not { } callerEmployeeId)
                return [];

            var teamIds = await directReportsReader.GetAllDescendantIdsAsync(
                companyId, callerEmployeeId, cancellationToken);

            if (teamIds.Count == 0)
                return [];

            employeeIds = teamIds;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.AssignedEmployeeId != null
                     && t.DueDate != null && t.DueDate < today
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress));

        if (employeeIds is not null)
            query = query.Where(t => employeeIds.Contains(t.AssignedEmployeeId!.Value));

        var viewerId = currentUser.UserId;
        if (hrScope && viewerId is { } viewer)
            query = query.Where(t => t.AssignedEmployeeId != viewer);

        var overdue = await query
            .Select(t => new { t.Id, t.Title, t.DueDate, AssignedEmployeeId = t.AssignedEmployeeId!.Value })
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, overdue.Select(t => t.AssignedEmployeeId).Distinct(), cancellationToken);

        return overdue.Select(t =>
        {
            departments.TryGetValue(t.AssignedEmployeeId, out var dept);
            return new WorkloadAction(
                EmployeeId: t.AssignedEmployeeId,
                EmployeeName: dept?.EmployeeName ?? t.AssignedEmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: t.Title,
                ActionCategory: ActionCategory,
                DueDate: t.DueDate,
                AssignedTo: dept?.EmployeeName,
                Status: "Overdue",
                DeepLinkUrl: $"/companies/{companyId}/tasks/{t.Id}",
                TaskId: t.Id,
                IsOwnerActionable: !hrScope,
                OwnerLabel: hrScope ? OwnerLabelFor(dept?.EmployeeName) : null,
                VisibilityReason: hrScope ? $"Shown so you can monitor it. {OwnerLabelFor(dept?.EmployeeName)}." : null);
        }).ToList();
    }

    private static string OwnerLabelFor(string? assigneeName) =>
        string.IsNullOrWhiteSpace(assigneeName) ? "Assigned to another employee" : $"Assigned to {assigneeName}";
}
