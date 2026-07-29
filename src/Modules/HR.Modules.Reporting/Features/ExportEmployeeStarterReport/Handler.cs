using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportEmployeeStarterReport;

internal sealed class ExportEmployeeStarterReportHandler(
    IEmployeeStarterReader employeeStarterReader,
    IReportExporter reportExporter)
{
    private const int MaxExportRows = 50_000;

    private static readonly string[] ColumnHeaders =
    [
        "Name", "Start Date", "Recruiter", "Department", "Position", "Onboarding Status", "Probation Status",
    ];

    public async Task<Result<ExportEmployeeStarterReportResponse>> HandleAsync(
        ExportEmployeeStarterReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            LocationId: request.LocationId,
            PositionProfileId: request.PositionProfileId,
            EmploymentTypeId: request.EmploymentTypeId,
            DateRangeStart: request.DateRangeStart,
            DateRangeEnd: request.DateRangeEnd);

        var pagination = new Pagination(PageNumber: 1, PageSize: MaxExportRows);

        var result = await employeeStarterReader.GetEmployeeStartersAsync(
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
                item.StartDate.ToString("yyyy-MM-dd"),
                item.Recruiter,
                item.Department,
                item.Position,
                item.OnboardingStatus,
                item.ProbationStatus,
            })
            .ToList();

        var exportData = new ReportExportData("Employee Starter Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportEmployeeStarterReportResponse(file));
    }
}
