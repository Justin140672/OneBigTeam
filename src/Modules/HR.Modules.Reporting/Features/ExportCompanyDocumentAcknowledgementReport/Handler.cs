using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;

internal sealed class ExportCompanyDocumentAcknowledgementReportHandler(
    GetCompanyDocumentAcknowledgementReportHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Document", "Employee", "Acknowledged", "Acknowledged At",
    ];

    public async Task<Result<ExportCompanyDocumentAcknowledgementReportResponse>> HandleAsync(
        ExportCompanyDocumentAcknowledgementReportRequest request,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetCompanyDocumentAcknowledgementReportRequest(request.CompanyId),
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportCompanyDocumentAcknowledgementReportResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.DocumentTitle,
                item.EmployeeName,
                item.Acknowledged.ToString(),
                item.AcknowledgedAt?.ToString("yyyy-MM-dd HH:mm"),
            })
            .ToList();

        var exportData = new ReportExportData("Company Document Acknowledgement Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportCompanyDocumentAcknowledgementReportResponse(file));
    }
}
