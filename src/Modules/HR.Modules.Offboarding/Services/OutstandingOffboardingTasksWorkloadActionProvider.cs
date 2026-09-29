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
    IOpenTaskBySourceEntityReader taskReader) : IWorkloadActionProvider
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
                    DeepLinkUrl: "",
                    TaskId: linkedTaskId));
            }
        }

        return actions;
    }
}
