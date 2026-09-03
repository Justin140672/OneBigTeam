using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for employee accounts awaiting disablement.
/// HR-only. Identity.Features.OnOffboardingPlanCompleted.Handler already auto-disables an account
/// the moment its OffboardingPlan completes, so an account only sits "awaiting disablement" in the
/// gap between an employee's LastWorkingDay passing and their offboarding plan actually completing.
/// That gap-detection data (LastWorkingDay, plan Status) is owned by HR.Modules.Offboarding, so this
/// provider composes IOffboardingReportReader (Offboarding's own cross-module reader contract,
/// already consumed the same way in the opposite direction by
/// GetOffboardingProgressReport/Handler.cs via IEmployeeUserAccountStatusReader — this is the
/// symmetric case) rather than duplicating offboarding-plan logic inside Identity.
/// </summary>
internal sealed class EmployeeAccountsAwaitingDisablementWorkloadActionProvider(
    IdentityDbContext dbContext,
    IOffboardingReportReader offboardingReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Employee Accounts Awaiting Disablement";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var offboardingItems = await offboardingReportReader.GetOffboardingReportAsync(companyId, cancellationToken);

        var pastLastWorkingDay = offboardingItems
            .Where(i => i.LastWorkingDay <= today && i.Status != "Completed")
            .ToList();

        if (pastLastWorkingDay.Count == 0)
            return [];

        var employeeIds = pastLastWorkingDay.Select(i => i.EmployeeId).ToList();

        var stillActiveUserIds = await dbContext.Users
            .AsNoTracking()
            .Where(u => employeeIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (stillActiveUserIds.Count == 0)
            return [];

        var stillActiveSet = stillActiveUserIds.ToHashSet();
        var relevant = pastLastWorkingDay.Where(i => stillActiveSet.Contains(i.EmployeeId)).ToList();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, relevant.Select(i => i.EmployeeId), cancellationToken);

        return relevant.Select(item =>
        {
            departments.TryGetValue(item.EmployeeId, out var dept);

            return new WorkloadAction(
                EmployeeId: item.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Disable Account",
                ActionCategory: ActionCategory,
                DueDate: item.LastWorkingDay,
                AssignedTo: null,
                Status: "Access Not Yet Disabled",
                DeepLinkUrl: $"/companies/{companyId}/user-administration/{item.EmployeeId}");
        }).ToList();
    }
}
