using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Offboarding.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for outstanding offboarding tasks. Reuses
/// IOffboardingReportReader (already used by GetOffboardingProgressReport/Handler.cs). HR-only,
/// matching GetOffboardingProgressReport's "reporting:view-hr" policy tier — offboarding has no
/// manager-scoped tier, unlike onboarding/probation.
/// </summary>
internal sealed class OutstandingOffboardingTasksWorkloadActionProvider(
    IOffboardingReportReader offboardingReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    IOpenTaskBySourceEntityReader taskReader) : IWorkloadActionProvider
{
    public string ActionCategory => "Outstanding Offboarding Tasks";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var items = await offboardingReportReader.GetOffboardingReportAsync(companyId, cancellationToken);
        if (items.Count == 0)
            return [];

        var employeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, employeeIds, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Each outstanding offboarding task is actioned via its own Task (TaskActionType.Complete,
        // keyed by the OffboardingTask id as SourceEntityId — see
        // CompleteOffboardingTaskFromTaskAction/OffboardingTaskSynchronizer.cs). Resolve the exact
        // linked task per offboarding task rather than matching by title/employee.
        var allTaskIds = items.SelectMany(i => i.OutstandingTaskIds ?? []).ToList();
        var openTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, allTaskIds, cancellationToken, TaskActionType.Complete);

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

                actions.Add(new WorkloadAction(
                    EmployeeId: item.EmployeeId,
                    EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: title,
                    ActionCategory: ActionCategory,
                    DueDate: item.LastWorkingDay,
                    AssignedTo: null,
                    Status: item.LastWorkingDay < today ? "Overdue" : "Outstanding",
                    // No employee-profile fallback: this category is entirely task-backed. See
                    // OutstandingOnboardingTasksWorkloadActionProvider for the same pattern.
                    DeepLinkUrl: "",
                    TaskId: linkedTaskId));
            }
        }

        return actions;
    }
}
