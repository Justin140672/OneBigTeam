using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal sealed class GetSicknessReportHandler(
    ISicknessReportReader sicknessReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader)
{
    public async Task<Result<GetSicknessReportResponse>> HandleAsync(
        GetSicknessReportRequest request,
        CancellationToken cancellationToken)
    {
        var records = await sicknessReportReader.GetSicknessRecordsAsync(
            request.CompanyId, request.StartDate, request.EndDate, cancellationToken);

        var employeeIds = records.Select(r => r.EmployeeId).ToHashSet();
        var departments = employeeIds.Count > 0
            ? await employeeDepartmentReader.GetDepartmentsAsync(request.CompanyId, employeeIds, cancellationToken)
            : new Dictionary<Guid, EmployeeDepartmentInfo>();

        var grouped = request.GroupBy switch
        {
            SicknessReportGroupBy.Department => records
                .GroupBy(r => departments.TryGetValue(r.EmployeeId, out var d) ? d.DepartmentId?.ToString() ?? "none" : "none")
                .Select(g => new SicknessReportGroupRow(
                    g.Key,
                    departments.Values.FirstOrDefault(d => d.DepartmentId?.ToString() == g.Key)?.DepartmentName ?? "No Department",
                    g.Count(),
                    g.Sum(r => r.DaysAbsent),
                    0))
                .ToList(),

            _ => records
                .GroupBy(r => r.EmployeeId)
                .Select(g => new SicknessReportGroupRow(
                    g.Key.ToString(),
                    departments.TryGetValue(g.Key, out var d) ? d.EmployeeName : g.Key.ToString(),
                    g.Count(),
                    g.Sum(r => r.DaysAbsent),
                    0))
                .ToList(),
        };

        return Result.Success(new GetSicknessReportResponse(grouped));
    }
}
