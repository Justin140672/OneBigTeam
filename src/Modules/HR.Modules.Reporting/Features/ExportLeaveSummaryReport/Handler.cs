using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportLeaveSummaryReport;

internal sealed class ExportLeaveSummaryReportHandler(
    GetLeaveSummaryReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Group", "Entitlement Days", "Booked Days", "Approved Days", "Remaining Days", "Pending Requests",
    ];

    public async Task<Result<ExportLeaveSummaryReportResponse>> HandleAsync(
        ExportLeaveSummaryReportRequest request,
        bool callerIsHr,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetLeaveSummaryReportRequest(request.CompanyId, request.PolicyYear, request.DepartmentId, request.LeaveTypeId, request.GroupBy),
            callerIsHr,
            callerEmployeeId,
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportLeaveSummaryReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.GroupLabel,
                item.EntitlementDays.ToString("0.##"),
                item.BookedDays.ToString("0.##"),
                item.ApprovedDays.ToString("0.##"),
                item.RemainingDays.ToString("0.##"),
                item.PendingRequestCount.ToString(),
            })
            .ToList();

        var exportData = new ReportExportData("Leave Summary Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportLeaveSummaryReportResponse(
            file, getResult.Value!.TotalCount, getResult.Value!.IsTruncated));
    }
}
