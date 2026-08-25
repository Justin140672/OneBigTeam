using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;

internal sealed class ExportEmployeeDirectoryReportHandler(
    IEmployeeDirectoryReader employeeDirectoryReader,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee Number", "Name", "Department", "Position", "Manager",
        "Employment Type", "Start Date", "Status", "Work Location", "Email",
    ];

    public async Task<Result<ExportEmployeeDirectoryReportResponse>> HandleAsync(
        ExportEmployeeDirectoryReportRequest request,
        CancellationToken cancellationToken)
    {
        var filter = new ReportFilterCriteria(
            DepartmentId: request.DepartmentId,
            LocationId: request.LocationId,
            PositionProfileId: request.PositionProfileId,
            ManagerId: request.ManagerId,
            EmploymentTypeId: request.EmploymentTypeId,
            DateRangeStart: request.DateRangeStart,
            DateRangeEnd: request.DateRangeEnd,
            EmployeeStatus: request.EmployeeStatus);

        var pagination = new Pagination(PageNumber: 1, PageSize: ReportLimits.ExportRowLimit);

        var result = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            request.CompanyId,
            filter,
            pagination,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        var rows = result.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.EmployeeNumber,
                item.Name,
                item.Department,
                item.Position,
                item.Manager,
                item.EmploymentType,
                item.StartDate.ToString("yyyy-MM-dd"),
                item.Status,
                item.WorkLocation,
                item.Email,
            })
            .ToList();

        var exportData = new ReportExportData("Employee Directory", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        var isTruncated = result.TotalCount > ReportLimits.ExportRowLimit;

        return Result.Success(new ExportEmployeeDirectoryReportResponse(file, result.TotalCount, isTruncated));
    }
}
