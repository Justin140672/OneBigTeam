using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportDocumentComplianceReport;

internal sealed class ExportDocumentComplianceReportHandler(
    GetDocumentComplianceReportHandler getHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "document-compliance";

    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Required", "Uploaded", "Missing", "Expiring Soon", "Expired", "Missing Documents",
    ];

    public async Task<Result<ExportDocumentComplianceReportResponse>> HandleAsync(
        ExportDocumentComplianceReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var getResult = await getHandler.HandleAsync(
                new GetDocumentComplianceReportRequest(request.CompanyId, request.PositionProfileId),
                cancellationToken);

            if (getResult.IsFailure)
            {
                await auditor.PublishFailureAsync(
                    request.CompanyId, ReportId, request.Format.ToString(),
                    managerScopeApplied: false, request, getResult.Error.Message, cancellationToken);
                return Result.Failure<ExportDocumentComplianceReportResponse>(getResult.Error);
            }

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

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), getResult.Value!.TotalEmployees,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportDocumentComplianceReportResponse(
                file, getResult.Value!.TotalEmployees, getResult.Value!.IsTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportDocumentComplianceReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
