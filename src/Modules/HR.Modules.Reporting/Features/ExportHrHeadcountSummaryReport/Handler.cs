using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportHrHeadcountSummaryReport;

internal sealed class ExportHrHeadcountSummaryReportHandler(
    IHrHeadcountSummaryReader hrHeadcountSummaryReader,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Department", "Location", "Position", "Employment Type",
        "Employee Status", "Start Date", "Leaving Date", "FTE",
    ];

    public async Task<Result<ExportHrHeadcountSummaryReportResponse>> HandleAsync(
        ExportHrHeadcountSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            LocationId: request.LocationId,
            EmploymentTypeId: request.EmploymentTypeId,
            EmployeeStatus: request.EmployeeStatus);

        var result = await hrHeadcountSummaryReader.GetHeadcountSummaryAsync(request.CompanyId, filter, cancellationToken);

        var rows = result.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.EmployeeName,
                item.Department,
                item.Location,
                item.Position,
                item.EmploymentType,
                item.Status,
                item.StartDate.ToString("yyyy-MM-dd"),
                item.LeavingDate?.ToString("yyyy-MM-dd"),
                item.Fte?.ToString("0.00"),
            })
            .ToList();

        var exportData = new ReportExportData("HR Headcount Summary", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportHrHeadcountSummaryReportResponse(file));
    }
}
