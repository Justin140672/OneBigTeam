using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;

internal sealed class ExportEmployeeLeaverReportHandler(
    IEmployeeLeaverReader employeeLeaverReader,
    IReportExporter reportExporter)
{
    private const int MaxExportRows = 50_000;

    private static readonly string[] ColumnHeaders =
    [
        "Name", "Leaving Date", "Last Working Day", "Department", "Position", "Reason",
        "Offboarding Status", "Account Status",
    ];

    public async Task<Result<ExportEmployeeLeaverReportResponse>> HandleAsync(
        ExportEmployeeLeaverReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            PositionProfileId: request.PositionProfileId,
            DateRangeStart: request.DateRangeStart,
            DateRangeEnd: request.DateRangeEnd);

        var pagination = new Pagination(PageNumber: 1, PageSize: MaxExportRows);

        var result = await employeeLeaverReader.GetEmployeeLeaversAsync(
            request.CompanyId,
            filter,
            pagination,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        var rows = result.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.Name,
                item.LeavingDate?.ToString("yyyy-MM-dd"),
                item.LastWorkingDay?.ToString("yyyy-MM-dd"),
                item.Department,
                item.Position,
                item.Reason,
                item.OffboardingStatus,
                item.AccountStatus,
            })
            .ToList();

        var exportData = new ReportExportData("Employee Leaver Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportEmployeeLeaverReportResponse(file));
    }
}
