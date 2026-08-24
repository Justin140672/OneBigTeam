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
                .Select(g => BuildRow(
                    g.Key,
                    departments.Values.FirstOrDefault(d => d.DepartmentId?.ToString() == g.Key)?.DepartmentName ?? "No Department",
                    g.Count(),
                    g.Sum(r => r.DaysAbsent)))
                .ToList(),

            _ => records
                .GroupBy(r => r.EmployeeId)
                .Select(g => BuildRow(
                    g.Key.ToString(),
                    departments.TryGetValue(g.Key, out var d) ? d.EmployeeName : g.Key.ToString(),
                    g.Count(),
                    g.Sum(r => r.DaysAbsent)))
                .ToList(),
        };

        return Result.Success(new GetSicknessReportResponse(grouped));
    }

    /// <summary>
    /// Bradford Factor = S^2 * D (S = separate absence spells, D = total days absent), evaluated
    /// over the report's own requested date range — see the comment on SicknessReportGroupRow for
    /// why there is no separately-enforced rolling window.
    /// </summary>
    private static SicknessReportGroupRow BuildRow(string groupKey, string groupLabel, int absenceCount, decimal daysAbsent)
    {
        var bradfordScore = (int)(absenceCount * absenceCount * daysAbsent);
        return new SicknessReportGroupRow(groupKey, groupLabel, absenceCount, daysAbsent, bradfordScore);
    }
}
