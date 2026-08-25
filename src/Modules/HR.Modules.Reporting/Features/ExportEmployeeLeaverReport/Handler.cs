using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;

internal sealed class ExportEmployeeLeaverReportHandler(
    IEmployeeLeaverReader employeeLeaverReader,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "employee-leavers";

    private static readonly string[] ColumnHeaders =
    [
        "Name", "Leaving Date", "Last Working Day", "Department", "Position", "Reason",
        "Offboarding Status", "Account Status",
    ];

    public async Task<Result<ExportEmployeeLeaverReportResponse>> HandleAsync(
        ExportEmployeeLeaverReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = new ReportFilterCriteria(
                DepartmentId: request.DepartmentId,
                PositionProfileId: request.PositionProfileId,
                DateRangeStart: request.DateRangeStart,
                DateRangeEnd: request.DateRangeEnd);

            var pagination = new Pagination(PageNumber: 1, PageSize: ReportLimits.ExportRowLimit);

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

            var isTruncated = result.TotalCount > ReportLimits.ExportRowLimit;

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), result.TotalCount,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportEmployeeLeaverReportResponse(file, result.TotalCount, isTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportEmployeeLeaverReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
