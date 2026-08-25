using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportDocumentComplianceReport;

internal sealed class ExportDocumentComplianceReportHandler(
    GetDocumentComplianceReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Required", "Uploaded", "Missing", "Expiring Soon", "Expired", "Missing Documents",
    ];

    public async Task<Result<ExportDocumentComplianceReportResponse>> HandleAsync(
        ExportDocumentComplianceReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetDocumentComplianceReportRequest(request.CompanyId, request.PositionProfileId),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportDocumentComplianceReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.EmployeeName,
                item.RequiredCount.ToString(),
                item.UploadedCount.ToString(),
                item.MissingCount.ToString(),
                item.ExpiringSoonCount.ToString(),
                item.ExpiredCount.ToString(),
                string.Join("; ", item.MissingDocumentTypeNames),
            })
            .ToList();

        var exportData = new ReportExportData("Document Compliance Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportDocumentComplianceReportResponse(
            file, getResult.Value!.TotalEmployees, getResult.Value!.IsTruncated));
    }
}
