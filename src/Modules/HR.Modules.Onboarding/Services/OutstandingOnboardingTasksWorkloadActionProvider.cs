using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Onboarding.Services;

internal sealed class OutstandingOnboardingTasksWorkloadActionProvider(
    IOnboardingReportReader onboardingReportReader,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    IOpenTaskBySourceEntityReader taskReader,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Outstanding Onboarding Tasks";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? employeeIds = null;
        Guid? managerCallerId = null;
        if (requestedScope == WorkloadScope.Hr)
        {
            var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
            if (!callerIsHr)
                return [];
        }
        else
        {
            var callerIsManager = (await authorizationService.AuthorizeAsync(caller, "reporting:view-onboarding")).Succeeded;
            if (!callerIsManager)
                return [];

            if (currentUser.UserId is not { } callerEmployeeId)
                return [];

            var teamIds = await directReportsReader.GetAllDescendantIdsAsync(
                companyId, callerEmployeeId, cancellationToken);

            if (teamIds.Count == 0)
                return [];

            employeeIds = teamIds;
            managerCallerId = callerEmployeeId;
        }

        var items = await onboardingReportReader.GetOnboardingReportAsync(companyId, employeeIds, cancellationToken);
        if (items.Count == 0)
            return [];

        var allEmployeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, allEmployeeIds, cancellationToken);

        var allTaskIds = items.SelectMany(i => i.OutstandingTasks.Select(t => t.TaskId)).ToList();
        var openTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, allTaskIds, cancellationToken, TaskActionType.Complete);

        // Manager workspace: a report's outstanding onboarding task is not necessarily one the
        // manager may open. GetTask only authorizes the assignee, a manager anywhere in the
        // ASSIGNEE's reporting line, or an HR Administrator — and unassigned tasks (e.g. the
        // default "Set up workstation and system access" task, which sits in the HR Inbox) are
        // HR-only. Previously every such row carried its TaskId and rendered as "Open task", and
        // activating it opened a Task View dialog that GetTask then rejected ("This task could not
        // be found..."). Keep the row visible for oversight but mark it non-actionable unless the
        // task's effective assignee is the manager themself or someone in their reporting
        // sub-tree — exactly the hierarchy GetTask enforces. The HR workspace is unaffected
        // (reporting:view-hr callers are HR Administrators, who may open any task).
        HashSet<Guid>? managerAccessibleAssignees = null;
        IReadOnlyDictionary<Guid, Guid?> assigneesByTaskId = new Dictionary<Guid, Guid?>();
        if (managerCallerId is { } managerId)
        {
            managerAccessibleAssignees = [.. employeeIds!, managerId];
            assigneesByTaskId = await taskReader.GetTaskAssigneesAsync(
                companyId, openTaskIds.Values, cancellationToken);
        }

        var actions = new List<WorkloadAction>();
        foreach (var item in items)
        {
            departments.TryGetValue(item.EmployeeId, out var dept);

            foreach (var task in item.OutstandingTasks)
            {
                var linkedTaskId = openTaskIds.TryGetValue(task.TaskId, out var tid) ? tid : (Guid?)null;

                var isOwnerActionable = true;
                string? ownerLabel = null;
                if (managerAccessibleAssignees is not null && linkedTaskId is { } linked)
                {
                    var assignee = assigneesByTaskId.TryGetValue(linked, out var a) ? a : null;
                    isOwnerActionable = assignee is { } assigneeId && managerAccessibleAssignees.Contains(assigneeId);
                    if (!isOwnerActionable)
                        ownerLabel = assignee is null ? "Unassigned — owned by HR" : "Assigned outside your team";
                }

                actions.Add(new WorkloadAction(
                    EmployeeId: item.EmployeeId,
                    EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: task.Title,
                    ActionCategory: ActionCategory,
                    DueDate: task.DueDate,
                    AssignedTo: task.Owner,
                    Status: task.IsOverdue ? "Overdue" : "Outstanding",
                    DeepLinkUrl: "",
                    TaskId: linkedTaskId,
                    IsOwnerActionable: isOwnerActionable,
                    OwnerLabel: ownerLabel));
            }
        }

        return actions;
    }
}
