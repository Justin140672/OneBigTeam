using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

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
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (requestedScope == WorkloadScope.Hr)
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

        var taskIdsByRequest = await taskReader.GetOpenTaskIdsAsync(
            companyId, pending.Select(p => p.Id), cancellationToken, TaskActionType.Approve);

        var hrScope = requestedScope == WorkloadScope.Hr;
        var viewerTeamIds = employeeIds;
        if (hrScope && currentUser.UserId is { } viewerId)
            viewerTeamIds = await directReportsReader.GetAllDescendantIdsAsync(companyId, viewerId, cancellationToken);

        IReadOnlyDictionary<Guid, Guid?> assigneesByTask = new Dictionary<Guid, Guid?>();
        IReadOnlyDictionary<Guid, EmployeeDepartmentInfo> assigneeNames = new Dictionary<Guid, EmployeeDepartmentInfo>();
        if (hrScope && taskIdsByRequest.Count > 0)
        {
            assigneesByTask = await taskReader.GetTaskAssigneesAsync(
                companyId, taskIdsByRequest.Values, cancellationToken);
            var assigneeIds = assigneesByTask.Values.Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
            if (assigneeIds.Count > 0)
                assigneeNames = await employeeDepartmentReader.GetDepartmentsAsync(companyId, assigneeIds, cancellationToken);
        }

        return pending.Select(p =>
        {
            departments.TryGetValue(p.EmployeeId, out var dept);
            var taskId = taskIdsByRequest.TryGetValue(p.Id, out var tid) ? tid : (Guid?)null;

            var decision = new WorkloadOwnerDecision(WorkloadActionability.CanAct, null, null);
            if (hrScope)
            {
                Guid? assignee = taskId is { } linked && assigneesByTask.TryGetValue(linked, out var a) ? a : null;
                string? assigneeName = assignee is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var info) ? info.EmployeeName : null;
                decision = WorkloadOwnership.ForHrViewer(
                    assignee, currentUser.UserId, unassignedBelongsToHr: false, assigneeName, "Owned by the employee's manager",
                    viewerManagesSubject: WorkloadOwnership.Manages(viewerTeamIds, p.EmployeeId),
                    viewerManagesAssignee: WorkloadOwnership.Manages(viewerTeamIds, assignee));
            }

            var deepLink = decision.IsOwnerActionable && taskId is null
                ? WorkloadOwnership.EmployeeTabUrl(companyId, p.EmployeeId, "leave")
                : "";

            return new WorkloadAction(
                EmployeeId: p.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? p.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Approve Leave Request",
                ActionCategory: ActionCategory,
                DueDate: p.StartDate,
                AssignedTo: null,
                Status: "Pending",
                DeepLinkUrl: deepLink,
                TaskId: taskId,
                IsOwnerActionable: decision.IsOwnerActionable,
                OwnerLabel: decision.OwnerLabel,
                VisibilityReason: decision.VisibilityReason);
        }).ToList();
    }
}
