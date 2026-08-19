using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Onboarding.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for outstanding onboarding tasks. Reuses
/// IOnboardingReportReader (already used by GetOnboardingProgressReport/Handler.cs) rather than
/// querying OnboardingDbContext directly, so the "outstanding task" definition stays in one place.
/// Row-scoping mirrors GetOnboardingProgressReport/Handler.cs exactly: HR sees every outstanding
/// task company-wide, a Manager sees only their own direct reports' tasks.
/// </summary>
internal sealed class OutstandingOnboardingTasksWorkloadActionProvider(
    IOnboardingReportReader onboardingReportReader,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Outstanding Onboarding Tasks";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;

        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            var callerIsManager = (await authorizationService.AuthorizeAsync(caller, "reporting:view-onboarding")).Succeeded;
            if (!callerIsManager)
                return [];

            // NOT caller.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
            // resolved Employee/UserId. ICurrentUser.UserId reads off the ambient HttpContext, safe
            // even from this provider's own DI scope.
            if (currentUser.UserId is not { } callerEmployeeId)
                return [];

            var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
                companyId, callerEmployeeId, cancellationToken);

            if (directReportIds.Count == 0)
                return [];

            employeeIds = directReportIds;
        }

        var items = await onboardingReportReader.GetOnboardingReportAsync(companyId, employeeIds, cancellationToken);
        if (items.Count == 0)
            return [];

        var allEmployeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, allEmployeeIds, cancellationToken);

        var actions = new List<WorkloadAction>();
        foreach (var item in items)
        {
            departments.TryGetValue(item.EmployeeId, out var dept);

            foreach (var task in item.OutstandingTasks)
            {
                actions.Add(new WorkloadAction(
                    EmployeeId: item.EmployeeId,
                    EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: task.Title,
                    ActionCategory: ActionCategory,
                    DueDate: task.DueDate,
                    AssignedTo: task.Owner,
                    Status: task.IsOverdue ? "Overdue" : "Outstanding",
                    DeepLinkUrl: $"/companies/{companyId}/employees/{item.EmployeeId}/view"));
            }
        }

        return actions;
    }
}
