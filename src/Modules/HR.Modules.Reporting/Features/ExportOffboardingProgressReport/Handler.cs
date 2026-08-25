using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetOffboardingProgressReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportOffboardingProgressReport;

internal sealed class ExportOffboardingProgressReportHandler(
    GetOffboardingProgressReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "offboarding-progress";

    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Last Working Day", "Status", "Outstanding Tasks", "Access Disabled", "Documents Returned", "Assets Returned",
    ];

    public async Task<Result<ExportOffboardingProgressReportResponse>> HandleAsync(
        ExportOffboardingProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetOffboardingProgressReportRequest(request.CompanyId),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportOffboardingProgressReportResponse>(getResult.Error);
            }

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

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.Items.Count,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportOffboardingProgressReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportOffboardingProgressReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
