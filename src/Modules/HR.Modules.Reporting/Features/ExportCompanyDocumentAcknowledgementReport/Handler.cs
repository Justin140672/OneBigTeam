using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;

internal sealed class ExportCompanyDocumentAcknowledgementReportHandler(
    GetCompanyDocumentAcknowledgementReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "document-acknowledgement";

    private static readonly string[] ColumnHeaders =
    [
        "Document", "Employee", "Acknowledged", "Acknowledged At",
    ];

    public async Task<Result<ExportCompanyDocumentAcknowledgementReportResponse>> HandleAsync(
        ExportCompanyDocumentAcknowledgementReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetCompanyDocumentAcknowledgementReportRequest(request.CompanyId),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportCompanyDocumentAcknowledgementReportResponse>(getResult.Error);
            }

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

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.Items.Count,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportCompanyDocumentAcknowledgementReportResponse(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportCompanyDocumentAcknowledgementReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
