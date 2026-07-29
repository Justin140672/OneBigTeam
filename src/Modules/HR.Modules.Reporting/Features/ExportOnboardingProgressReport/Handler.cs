using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetOnboardingProgressReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportOnboardingProgressReport;

internal sealed class ExportOnboardingProgressReportHandler(
    GetOnboardingProgressReportHandler getHandler,
    IReportExporter reportExporter)
{
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
        var getResult = await getHandler.HandleAsync(
            new GetOnboardingProgressReportRequest(request.CompanyId, request.OverdueOnly),
            callerIsHr,
            callerEmployeeId,
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportOnboardingProgressReportResponse>(getResult.Error);

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

        return Result.Success(new ExportOnboardingProgressReportResponse(file));
    }
}
