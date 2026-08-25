using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportLeaveCalendarReport;

internal sealed class ExportLeaveCalendarReportHandler(
    ILeaveCalendarReader leaveCalendarReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Department", "Leave Start", "Leave End", "Leave Type", "Duration (Days)", "Approval Status",
    ];

    public async Task<Result<ExportLeaveCalendarReportResponse>> HandleAsync(
        ExportLeaveCalendarReportRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await leaveCalendarReader.GetLeaveCalendarAsync(
            request.CompanyId, employeeIds: null, request.Year, request.Month, cancellationToken);

        var employeeIds = rows.Select(r => r.EmployeeId).ToHashSet();
        var departments = employeeIds.Count > 0
            ? await employeeDepartmentReader.GetDepartmentsAsync(request.CompanyId, employeeIds, cancellationToken)
            : new Dictionary<Guid, EmployeeDepartmentInfo>();

        var filtered = rows.AsEnumerable();
        if (request.DepartmentId is not null)
        {
            filtered = filtered.Where(r =>
                departments.TryGetValue(r.EmployeeId, out var dept) && dept.DepartmentId == request.DepartmentId);
        }

        var filteredList = filtered.ToList();
        var totalCount = filteredList.Count;
        var isTruncated = totalCount > ReportLimits.ExportRowLimit;

        var exportRows = filteredList
            .Take(ReportLimits.ExportRowLimit)
            .Select(r => (IReadOnlyList<string?>)new List<string?>
            {
                departments.TryGetValue(r.EmployeeId, out var dept) ? dept.EmployeeName : r.EmployeeId.ToString(),
                departments.TryGetValue(r.EmployeeId, out var d2) ? d2.DepartmentName : null,
                r.LeaveStart.ToString("yyyy-MM-dd"),
                r.LeaveEnd.ToString("yyyy-MM-dd"),
                r.LeaveTypeName,
                r.DurationDays.ToString("0.##"),
                r.ApprovalStatus,
            })
            .ToList();

        var exportData = new ReportExportData("Leave Calendar Export", ColumnHeaders, exportRows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportLeaveCalendarReportResponse(file, totalCount, isTruncated));
    }
}
