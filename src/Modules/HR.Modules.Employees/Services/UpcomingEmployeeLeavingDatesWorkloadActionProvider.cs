using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for upcoming employee leaving dates. HR-only,
/// same tier as <see cref="UpcomingEmployeeStartDatesWorkloadActionProvider"/>. Sourced from
/// EmployeeLeavingProcess (in-progress only — cancelled/completed processes are not upcoming
/// actions), which is the same entity IEmployeeLeaverReader composes for the Employee Leaver Report.
/// </summary>
internal sealed class UpcomingEmployeeLeavingDatesWorkloadActionProvider(
    EmployeesDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Upcoming Leaving Dates";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var leavers = await dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .Where(lp => lp.CompanyId == companyId
                      && lp.Status == LeavingProcessStatus.InProgress
                      && lp.LastWorkingDay >= today)
            .Select(lp => new { lp.EmployeeId, lp.LastWorkingDay })
            .ToListAsync(cancellationToken);

        if (leavers.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, leavers.Select(l => l.EmployeeId), cancellationToken);

        return leavers.Select(l =>
        {
            departments.TryGetValue(l.EmployeeId, out var dept);
            return new WorkloadAction(
                EmployeeId: l.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? l.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Prepare for Employee Departure",
                ActionCategory: ActionCategory,
                DueDate: l.LastWorkingDay,
                AssignedTo: null,
                Status: "Upcoming",
                DeepLinkUrl: $"/companies/{companyId}/employees/{l.EmployeeId}/view");
        }).ToList();
    }
}
