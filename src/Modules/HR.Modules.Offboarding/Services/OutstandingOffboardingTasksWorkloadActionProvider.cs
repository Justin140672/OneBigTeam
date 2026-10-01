using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Offboarding.Services;

internal sealed class OutstandingOffboardingTasksWorkloadActionProvider(
    IOffboardingReportReader offboardingReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    IOpenTaskBySourceEntityReader taskReader,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Outstanding Offboarding Tasks";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        if (requestedScope != WorkloadScope.Hr)
            return [];

        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var items = await offboardingReportReader.GetOffboardingReportAsync(companyId, cancellationToken);
        if (items.Count == 0)
            return [];

        var employeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, employeeIds, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var allTaskIds = items.SelectMany(i => i.OutstandingTaskIds ?? []).ToList();
        var openTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, allTaskIds, cancellationToken, TaskActionType.Complete);

        var assigneesByTask = openTaskIds.Count > 0
            ? await taskReader.GetTaskAssigneesAsync(companyId, openTaskIds.Values, cancellationToken)
            : new Dictionary<Guid, Guid?>();
        var assigneeIds = assigneesByTask.Values.Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
        var assigneeNames = assigneeIds.Count > 0
            ? await employeeDepartmentReader.GetDepartmentsAsync(companyId, assigneeIds, cancellationToken)
            : new Dictionary<Guid, EmployeeDepartmentInfo>();

        var actions = new List<WorkloadAction>();
        foreach (var item in items)
        {
            if (item.OutstandingTaskTitles.Count == 0)
                continue;

            departments.TryGetValue(item.EmployeeId, out var dept);

            for (var i = 0; i < item.OutstandingTaskTitles.Count; i++)
            {
                var title = item.OutstandingTaskTitles[i];
                var sourceTaskId = item.OutstandingTaskIds is { } ids && i < ids.Count ? ids[i] : (Guid?)null;
                var linkedTaskId = sourceTaskId is not null && openTaskIds.TryGetValue(sourceTaskId.Value, out var tid)
                    ? tid
                    : (Guid?)null;

                Guid? assignee = linkedTaskId is { } linked && assigneesByTask.TryGetValue(linked, out var a) ? a : null;
                string? assigneeName = assignee is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var info) ? info.EmployeeName : null;
                var decision = linkedTaskId is null
                    ? new WorkloadOwnerDecision(WorkloadActionability.CanAct, null, null)
                    : WorkloadOwnership.ForHrViewer(assignee, currentUser.UserId, unassignedBelongsToHr: true, assigneeName, "Assigned to another user");

                actions.Add(new WorkloadAction(
                    EmployeeId: item.EmployeeId,
                    EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: title,
                    ActionCategory: ActionCategory,
                    DueDate: item.LastWorkingDay,
                    AssignedTo: null,
                    Status: item.LastWorkingDay < today ? "Overdue" : "Outstanding",
                    DeepLinkUrl: "",
                    TaskId: linkedTaskId,
                    IsOwnerActionable: decision.IsOwnerActionable,
                    OwnerLabel: decision.OwnerLabel,
                    VisibilityReason: decision.VisibilityReason));
            }
        }

        return actions;
    }
}
