using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportSicknessReport;

internal sealed class ExportSicknessReportHandler(
    GetSicknessReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Group", "Absence Count", "Days Absent", "Bradford Score",
    ];

    public async Task<Result<ExportSicknessReportResponse>> HandleAsync(
        ExportSicknessReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetSicknessReportRequest(request.CompanyId, request.StartDate, request.EndDate, request.GroupBy),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportSicknessReportResponse>(getResult.Error);

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

        return Result.Success(new ExportSicknessReportResponse(
            file, getResult.Value!.TotalCount, getResult.Value!.IsTruncated));
    }
}
