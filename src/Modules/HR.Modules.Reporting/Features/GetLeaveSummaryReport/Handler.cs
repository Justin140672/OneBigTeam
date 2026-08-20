using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

internal sealed class GetLeaveSummaryReportHandler(
    ILeaveSummaryReader leaveSummaryReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IDirectReportsReader directReportsReader)
{
    public async Task<Result<GetLeaveSummaryReportResponse>> HandleAsync(
        GetLeaveSummaryReportRequest request,
        bool callerIsHr,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var policyYear = request.PolicyYear ?? DateTime.UtcNow.Year;

        // Row-level manager scoping: a non-HR caller (Manager only, per reporting:view-leave-summary
        // policy) is restricted to their own direct reports — never company-wide data — regardless
        // of any filter supplied. This mirrors the same hard-gate approach GetTeamSicknessToday uses.
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
                request.CompanyId, callerEmployeeId, cancellationToken);
            employeeIds = directReportIds.ToList();

            if (employeeIds.Count == 0)
                return Result.Success(new GetLeaveSummaryReportResponse([]));
        }

        var rows = await leaveSummaryReader.GetLeaveSummaryAsync(
            request.CompanyId, employeeIds, policyYear, cancellationToken);

        if (request.LeaveTypeId is not null)
        {
            rows = rows.Where(r => r.LeaveTypeId == request.LeaveTypeId.Value).ToList();
        }
        else if (request.GroupBy is LeaveSummaryGroupBy.Employee or LeaveSummaryGroupBy.Department)
        {
            // Bug fix: grouping by Employee/Department previously summed EntitlementDays (and
            // BookedDays/ApprovedDays/RemainingDays) across EVERY leave type that employee has a
            // balance for — Annual Leave + Sick Leave + Compassionate Leave + Parental Leave, etc.
            // all added into one number. Those are different, non-additive buckets (a "92 days"
            // combined figure was never a meaningful entitlement for anyone) — reported live as
            // showing 92 instead of the correct 23. Without an explicit leave type filter, these
            // two grouped views now reflect Annual Leave only, the one entitlement-bearing
            // "headline" leave type in this system (same convention as the Default Days
            // restriction on LeaveType — see LeaveTypeEdit.razor's IsAnnualLeave).
            rows = rows
                .Where(r => string.Equals(r.LeaveTypeName, "Annual Leave", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var allEmployeeIds = rows.Select(r => r.EmployeeId).ToHashSet();
        var departments = allEmployeeIds.Count > 0
            ? await employeeDepartmentReader.GetDepartmentsAsync(request.CompanyId, allEmployeeIds, cancellationToken)
            : new Dictionary<Guid, EmployeeDepartmentInfo>();

        if (request.DepartmentId is not null)
        {
            rows = rows
                .Where(r => departments.TryGetValue(r.EmployeeId, out var dept) && dept.DepartmentId == request.DepartmentId)
                .ToList();
        }

        var grouped = request.GroupBy switch
        {
            LeaveSummaryGroupBy.Department => rows
                .GroupBy(r => departments.TryGetValue(r.EmployeeId, out var d) ? d.DepartmentId?.ToString() ?? "none" : "none")
                .Select(g => new LeaveSummaryGroupRow(
                    g.Key,
                    departments.Values.FirstOrDefault(d => d.DepartmentId?.ToString() == g.Key)?.DepartmentName ?? "No Department",
                    g.Sum(r => r.EntitlementDays),
                    g.Sum(r => r.BookedDays),
                    g.Sum(r => r.ApprovedDays),
                    g.Sum(r => r.RemainingDays),
                    g.Sum(r => r.PendingRequestCount)))
                .ToList(),

            LeaveSummaryGroupBy.LeaveType => rows
                .GroupBy(r => r.LeaveTypeId.ToString())
                .Select(g => new LeaveSummaryGroupRow(
                    g.Key,
                    g.First().LeaveTypeName,
                    g.Sum(r => r.EntitlementDays),
                    g.Sum(r => r.BookedDays),
                    g.Sum(r => r.ApprovedDays),
                    g.Sum(r => r.RemainingDays),
                    g.Sum(r => r.PendingRequestCount)))
                .ToList(),

            _ => rows
                .GroupBy(r => r.EmployeeId)
                .Select(g => new LeaveSummaryGroupRow(
                    g.Key.ToString(),
                    departments.TryGetValue(g.Key, out var d) ? d.EmployeeName : g.Key.ToString(),
                    g.Sum(r => r.EntitlementDays),
                    g.Sum(r => r.BookedDays),
                    g.Sum(r => r.ApprovedDays),
                    g.Sum(r => r.RemainingDays),
                    g.Sum(r => r.PendingRequestCount)))
                .ToList(),
        };

        return Result.Success(new GetLeaveSummaryReportResponse(grouped));
    }
}
