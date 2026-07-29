using System.Security.Claims;
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
    IAuthorizationService authorizationService) : IWorkloadActionProvider
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

        var actions = new List<WorkloadAction>();
        foreach (var item in items)
        {
            if (item.OutstandingTaskTitles.Count == 0)
                continue;

            departments.TryGetValue(item.EmployeeId, out var dept);

            foreach (var title in item.OutstandingTaskTitles)
            {
                actions.Add(new WorkloadAction(
                    EmployeeId: item.EmployeeId,
                    EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: title,
                    ActionCategory: ActionCategory,
                    DueDate: item.LastWorkingDay,
                    AssignedTo: null,
                    Status: item.LastWorkingDay < today ? "Overdue" : "Outstanding",
                    DeepLinkUrl: $"/companies/{companyId}/employees/{item.EmployeeId}/view"));
            }
        }

        return actions;
    }
}
