using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetOffboardingProgressReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportOffboardingProgressReport;

internal sealed class ExportOffboardingProgressReportHandler(
    GetOffboardingProgressReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Last Working Day", "Status", "Outstanding Tasks", "Access Disabled", "Documents Returned", "Assets Returned",
    ];

    public async Task<Result<ExportOffboardingProgressReportResponse>> HandleAsync(
        ExportOffboardingProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetOffboardingProgressReportRequest(request.CompanyId),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportOffboardingProgressReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.EmployeeName,
                item.LastWorkingDay.ToString("yyyy-MM-dd"),
                item.Status,
                string.Join("; ", item.OutstandingTasks),
                item.AccessDisabled.ToString(),
                item.DocumentsReturned.ToString(),
                item.AssetsReturned.ToString(),
            })
            .ToList();

        var exportData = new ReportExportData("Offboarding Progress Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportOffboardingProgressReportResponse(file));
    }
}
