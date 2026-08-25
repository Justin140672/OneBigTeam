using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetAssetAssignmentReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportAssetAssignmentReport;

internal sealed class ExportAssetAssignmentReportHandler(
    GetAssetAssignmentReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "asset-assignment";

    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Asset", "Serial Number", "Assigned Date", "Return Status",
    ];

    public async Task<Result<ExportAssetAssignmentReportResponse>> HandleAsync(
        ExportAssetAssignmentReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetAssetAssignmentReportRequest(request.CompanyId),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportAssetAssignmentReportResponse>(getResult.Error);
            }

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

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.TotalAssignments,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportAssetAssignmentReportResponse(
                file, getResult.Value!.TotalAssignments, getResult.Value!.IsTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportAssetAssignmentReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
