using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetProbationReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportProbationReport;

internal sealed class ExportProbationReportHandler(
    GetProbationReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "probation-report";

    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Status", "Start Date", "Expected End Date", "Due Reviews", "Overdue Reviews",
    ];

    public async Task<Result<ExportProbationReportResponse>> HandleAsync(
        ExportProbationReportRequest request,
        bool callerIsHr,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var managerScopeApplied = !callerIsHr;

        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetProbationReportRequest(request.CompanyId),
                callerIsHr,
                callerEmployeeId,
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportProbationReportResponse>(getResult.Error);
            }

            var rows = getResult.Value!.Items
                .Select(item => (IReadOnlyList<string?>)new List<string?>
                {
                    item.EmployeeName,
                    item.Status,
                    item.StartDate.ToString("yyyy-MM-dd"),
                    item.ExpectedEndDate.ToString("yyyy-MM-dd"),
                    item.DueReviews.ToString(),
                    item.OverdueReviews.ToString(),
                })
                .ToList();

            var exportData = new ReportExportData("Probation Report", ColumnHeaders, rows);
            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.Items.Count,
                managerScopeApplied, request, cancellationToken);

            return Result.Success(new ExportProbationReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied, request, ex.Message, cancellationToken);
            return Result.Failure<ExportProbationReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
