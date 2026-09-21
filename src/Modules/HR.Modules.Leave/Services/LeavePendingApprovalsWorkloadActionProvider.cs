using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for pending leave requests. Row-scoping mirrors
/// GetLeaveSummaryReport/Handler.cs: HR sees every pending request company-wide, a Manager sees
/// their whole reporting sub-tree's pending requests (direct or indirect reports, per DSH-02),
/// and anyone else (plain Employee, Recruiter with
/// no management/HR role) sees nothing — self-enforced here rather than trusted from the caller.
/// </summary>
internal sealed class LeavePendingApprovalsWorkloadActionProvider(
    LeaveDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    IOpenTaskBySourceEntityReader taskReader,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Pending Leave Approvals";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        // Scope is driven by the EXPLICITLY requested workspace, never re-inferred from the
        // caller's full role set — a caller holding both HR and Manager roles must still only see
        // their own reporting sub-tree when the Manager workspace is requested.
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (requestedScope == WorkloadScope.Hr)
        {
            // A requested workspace is a display-routing signal only: re-verify the caller actually
            // holds HR access before honouring it, never trust it as authorization.
            var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
            if (!callerIsHr)
                return [];
        }
        else
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

        var query = dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Status == LeaveRequestStatus.Pending);

        if (employeeIds is not null)
            query = query.Where(r => employeeIds.Contains(r.EmployeeId));

        var pending = await query
            .Select(r => new { r.Id, r.EmployeeId, r.StartDate })
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, pending.Select(p => p.EmployeeId).Distinct(), cancellationToken);

        // A pending leave request is actioned via its approval Task (TaskActionType.Approve, keyed
        // by the leave request id as SourceEntityId — see LeaveRequestedHandler). Surface that task
        // id so a dashboard/attention-queue row opens the Task view dialog in place rather than
        // deep-linking to the employee page; falls back to the deep link when no open task exists.
        var taskIdsByRequest = await taskReader.GetOpenTaskIdsAsync(
            companyId, pending.Select(p => p.Id), cancellationToken, TaskActionType.Approve);

        // This category is entirely task-backed: a pending leave request is actioned via its
        // approval Task, opened in the Task View dialog (LeaveTaskPanel). No employee-profile deep
        // link is offered as a fallback — when the task cannot be resolved, DeepLinkUrl stays blank
        // so the dashboard shows an explicit "no longer available" state instead. DueDate is the
        // leave's own StartDate: an approval is only truly useful before the leave period begins,
        // so that is the meaningful "due by" date for this action, not the request's submission date.
        // A pending leave request's approval task is always assigned to the EMPLOYEE'S MANAGER
        // (see LeaveRequestedHandler) — never to HR. When this provider is composing the HR
        // workspace, HR is only being shown these rows for company-wide oversight; the item must
        // not be presented as something HR can click into and action from that list, even though
        // an HR Administrator's server-side override would separately allow it via GetTask/
        // CompleteTask. Actionability here always matches the true owner (the manager), regardless
        // of which workspace requested the data.
        var isHrOversightOnly = requestedScope == WorkloadScope.Hr;

        return pending.Select(p =>
        {
            departments.TryGetValue(p.EmployeeId, out var dept);
            var taskId = taskIdsByRequest.TryGetValue(p.Id, out var tid) ? tid : (Guid?)null;

            return new WorkloadAction(
                EmployeeId: p.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? p.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Approve Leave Request",
                ActionCategory: ActionCategory,
                DueDate: p.StartDate,
                AssignedTo: null,
                Status: "Pending",
                DeepLinkUrl: "",
                TaskId: taskId,
                IsOwnerActionable: !isHrOversightOnly,
                OwnerLabel: isHrOversightOnly ? "Owned by the employee's manager" : null);
        }).ToList();
    }
}
