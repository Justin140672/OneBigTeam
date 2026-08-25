using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportSicknessReport;

internal sealed class ExportSicknessReportHandler(
    GetSicknessReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "sickness-report";

    private static readonly string[] ColumnHeaders =
    [
        "Group", "Absence Count", "Days Absent", "Bradford Score",
    ];

    public async Task<Result<ExportSicknessReportResponse>> HandleAsync(
        ExportSicknessReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetSicknessReportRequest(request.CompanyId, request.StartDate, request.EndDate, request.GroupBy),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportSicknessReportResponse>(getResult.Error);
            }

            var rows = getResult.Value!.Items
                .Select(item => (IReadOnlyList<string?>)new List<string?>
                {
                    item.GroupLabel,
                    item.AbsenceCount.ToString(),
                    item.DaysAbsent.ToString("0.##"),
                    item.BradfordScore.ToString(),
                })
                .ToList();

            var exportData = new ReportExportData("Sickness Report", ColumnHeaders, rows);
            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.TotalCount,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportSicknessReportResponse(
                file, getResult.Value!.TotalCount, getResult.Value!.IsTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportSicknessReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
