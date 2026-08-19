using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for pending leave requests. Row-scoping mirrors
/// GetLeaveSummaryReport/Handler.cs: HR sees every pending request company-wide, a Manager sees
/// only their own direct reports' pending requests, and anyone else (plain Employee, Recruiter with
/// no management/HR role) sees nothing — self-enforced here rather than trusted from the caller.
/// </summary>
internal sealed class LeavePendingApprovalsWorkloadActionProvider(
    LeaveDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Pending Leave Approvals";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;

        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
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

        // No dedicated leave-approval screen exists yet in HR.Web, so the deep link routes to the
        // employee's profile page, which is where a Manager/HR user actions leave requests today —
        // documented interpretation, see OBT-721 ticket note on providers without a clean existing
        // "real screen" to link to. DueDate is the leave's own StartDate: an approval is only truly
        // useful before the leave period begins, so that is the meaningful "due by" date for this
        // action, not the request's submission date.
        return pending.Select(p =>
        {
            departments.TryGetValue(p.EmployeeId, out var dept);

            return new WorkloadAction(
                EmployeeId: p.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? p.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Approve Leave Request",
                ActionCategory: ActionCategory,
                DueDate: p.StartDate,
                AssignedTo: null,
                Status: "Pending",
                DeepLinkUrl: $"/companies/{companyId}/employees/{p.EmployeeId}/view");
        }).ToList();
    }
}
