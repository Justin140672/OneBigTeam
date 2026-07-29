using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetLeaveCalendarReport;

internal sealed class GetLeaveCalendarReportHandler(
    ILeaveCalendarReader leaveCalendarReader,
    IEmployeeDepartmentReader employeeDepartmentReader)
{
    // Export is the primary path per OBT-707, but this bounds the on-screen grid too.
    private const int MaxRows = 20_000;

    public async Task<Result<GetLeaveCalendarReportResponse>> HandleAsync(
        GetLeaveCalendarReportRequest request,
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

        var items = filtered
            .Take(MaxRows)
            .Select(r => new LeaveCalendarReportRow(
                r.EmployeeId,
                departments.TryGetValue(r.EmployeeId, out var dept) ? dept.EmployeeName : r.EmployeeId.ToString(),
                departments.TryGetValue(r.EmployeeId, out var d2) ? d2.DepartmentName : null,
                r.LeaveStart,
                r.LeaveEnd,
                r.LeaveTypeName,
                r.DurationDays,
                r.ApprovalStatus))
            .ToList();

        return Result.Success(new GetLeaveCalendarReportResponse(items));
    }
}
