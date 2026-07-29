using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for upcoming employee start dates. HR-only —
/// there is no manager-scoped tier for this category per the OBT-721 ticket. Queries
/// EmployeesDbContext directly (rather than IEmployeeStarterReader, which is paged/filtered for a
/// dedicated report UI) since this provider only needs a simple forward-looking window.
/// </summary>
internal sealed class UpcomingEmployeeStartDatesWorkloadActionProvider(
    EmployeesDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Upcoming Employee Start Dates";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var horizon = today.AddDays(30);

        var starters = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.StartDate >= today && e.StartDate <= horizon)
            .Select(e => new { e.Id, e.StartDate })
            .ToListAsync(cancellationToken);

        if (starters.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, starters.Select(s => s.Id), cancellationToken);

        return starters.Select(s =>
        {
            departments.TryGetValue(s.Id, out var dept);
            return new WorkloadAction(
                EmployeeId: s.Id,
                EmployeeName: dept?.EmployeeName ?? s.Id.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Prepare for New Starter",
                ActionCategory: ActionCategory,
                DueDate: s.StartDate,
                AssignedTo: null,
                Status: "Upcoming",
                DeepLinkUrl: $"/companies/{companyId}/employees/{s.Id}/view");
        }).ToList();
    }
}
