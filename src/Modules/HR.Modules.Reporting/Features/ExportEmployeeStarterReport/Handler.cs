using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportEmployeeStarterReport;

internal sealed class ExportEmployeeStarterReportHandler(
    IEmployeeStarterReader employeeStarterReader,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "employee-starters";

    private static readonly string[] ColumnHeaders =
    [
        "Name", "Start Date", "Recruiter", "Department", "Position", "Onboarding Status", "Probation Status",
    ];

    public async Task<Result<ExportEmployeeStarterReportResponse>> HandleAsync(
        ExportEmployeeStarterReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = new ReportFilterCriteria(
                DepartmentId: request.DepartmentId,
                LocationId: request.LocationId,
                PositionProfileId: request.PositionProfileId,
                EmploymentTypeId: request.EmploymentTypeId,
                DateRangeStart: request.DateRangeStart,
                DateRangeEnd: request.DateRangeEnd);

            var pagination = new Pagination(PageNumber: 1, PageSize: ReportLimits.ExportRowLimit);

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

            var isTruncated = result.TotalCount > ReportLimits.ExportRowLimit;

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), result.TotalCount,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportEmployeeStarterReportResponse(file, result.TotalCount, isTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportEmployeeStarterReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
