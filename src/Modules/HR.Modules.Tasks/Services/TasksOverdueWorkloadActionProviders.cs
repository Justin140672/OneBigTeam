using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report — self-scoped provider: every authenticated caller sees
/// their own overdue tasks (TaskItem.AssignedEmployeeId == caller), regardless of role. This is the
/// "Employee Tasks Overdue" category from the ticket.
///
/// Interpretation note (documented per OBT-721 ticket guidance, since TaskItem does not cleanly
/// separate "employee-owned" vs "manager-owned" tasks by any domain flag): this provider and
/// <see cref="ManagerTasksOverdueWorkloadActionProvider"/> below both query the same TaskItem table,
/// differing only in whose tasks they surface — self vs. direct-reports/company-wide. There is no
/// TaskItem.Category concept to split "employee kind" vs "manager kind" tasks by content.
/// </summary>
internal sealed class EmployeeTasksOverdueWorkloadActionProvider(
    TasksDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Employee Tasks Overdue";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        // NOT caller.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
        // resolved Employee/UserId. ICurrentUser.UserId reads off the ambient HttpContext, safe
        // even from this provider's own DI scope.
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

/// <summary>
/// OBT-721 "Manager Tasks Overdue" category: a Manager sees overdue tasks assigned to anyone in
/// their reporting sub-tree (direct or indirect reports, per DSH-02); HR sees every overdue task
/// company-wide. See the interpretation note on
/// <see cref="EmployeeTasksOverdueWorkloadActionProvider"/> above.
/// </summary>
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
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;

        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            // NOT caller.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
            // resolved Employee/UserId. ICurrentUser.UserId reads off the ambient HttpContext, safe
            // even from this provider's own DI scope.
            if (currentUser.UserId is not { } callerEmployeeId)
                return [];

            // DSH-02: a manager's dashboard scope is their entire reporting sub-tree (direct and
            // indirect reports). See specifications/architecture/11-manager-hierarchy-scope.md.
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
                TaskId: t.Id);
        }).ToList();
    }
}
