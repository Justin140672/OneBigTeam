using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetOnboardingProgressReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportOnboardingProgressReport;

internal sealed class ExportOnboardingProgressReportHandler(
    GetOnboardingProgressReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "onboarding-progress";

    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Plan Status", "Progress %", "Outstanding Tasks", "Has Overdue",
    ];

    public async Task<Result<ExportOnboardingProgressReportResponse>> HandleAsync(
        ExportOnboardingProgressReportRequest request,
        bool callerIsHr,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var managerScopeApplied = !callerIsHr;

        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetOnboardingProgressReportRequest(request.CompanyId, request.OverdueOnly),
                callerIsHr,
                callerEmployeeId,
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportOnboardingProgressReportResponse>(getResult.Error);
            }

            var rows = getResult.Value!.Items
                .Select(item => (IReadOnlyList<string?>)new List<string?>
                {
                    item.EmployeeName,
                    item.PlanStatus,
                    item.ProgressPercent.ToString(),
                    string.Join("; ", item.OutstandingTasks.Select(t => t.Title)),
                    item.HasOverdueTasks.ToString(),
                })
                .ToList();

            var exportData = new ReportExportData("Onboarding Progress Report", ColumnHeaders, rows);
            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.Items.Count,
                managerScopeApplied, request, cancellationToken);

            return Result.Success(new ExportOnboardingProgressReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied, request, ex.Message, cancellationToken);
            return Result.Failure<ExportOnboardingProgressReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
