using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetAssetAssignmentReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportAssetAssignmentReport;

internal sealed class ExportAssetAssignmentReportHandler(
    GetAssetAssignmentReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Asset", "Serial Number", "Assigned Date", "Return Status",
    ];

    public async Task<Result<ExportAssetAssignmentReportResponse>> HandleAsync(
        ExportAssetAssignmentReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetAssetAssignmentReportRequest(request.CompanyId),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportAssetAssignmentReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.EmployeeName,
                item.AssetName,
                item.SerialNumber,
                item.AssignedDate.ToString("yyyy-MM-dd"),
                item.ReturnStatus,
            })
            .ToList();

        var exportData = new ReportExportData("Asset Assignment Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportAssetAssignmentReportResponse(
            file, getResult.Value!.TotalAssignments, getResult.Value!.IsTruncated));
    }
}
