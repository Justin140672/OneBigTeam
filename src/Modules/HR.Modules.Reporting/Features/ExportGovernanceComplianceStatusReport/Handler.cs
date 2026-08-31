using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;

/// <summary>
/// ADM-08. Exports the Compliance Status governance report via the established reporting export
/// framework. Data comes from the ADM-02 <see cref="GetComplianceCentreHandler"/> composition; the
/// endpoint applies the same policy pair and the same filters as the on-screen report so the
/// exported scope matches. Compliance rows carry no salary / NI / token data.
/// </summary>
internal sealed class ExportGovernanceComplianceStatusReportHandler(
    GetComplianceCentreHandler complianceCentreHandler,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "governance-compliance-status";

    private static readonly string[] ColumnHeaders =
        ["Employee", "Department", "Category", "Detail", "Due Date", "Severity"];

    public async Task<Result<ExportGovernanceComplianceStatusReportResponse>> HandleAsync(
        ExportGovernanceComplianceStatusReportRequest request,
        CancellationToken cancellationToken)
    {
        var managerScopeApplied = request.ManagerId is not null;

        try
        {
            var centre = await complianceCentreHandler.HandleAsync(
                new GetComplianceCentreRequest(
                    request.CompanyId,
                    request.Category,
                    request.Department,
                    request.ManagerId,
                    request.DueDateStart,
                    request.DueDateEnd,
                    request.Severity),
                cancellationToken);

            var value = centre.Value!;

            var rows = value.Items
                .Select(i => (IReadOnlyList<string?>)new List<string?>
                {
                    i.EmployeeName,
                    i.Department,
                    i.CategoryLabel,
                    i.Detail,
                    i.DueDate?.ToString("yyyy-MM-dd"),
                    i.Severity,
                })
                .ToList();

            var exportData = new ReportExportData("Governance — Compliance Status", ColumnHeaders, rows);
            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), value.TotalCount,
                managerScopeApplied, request, cancellationToken);

            return Result.Success(new ExportGovernanceComplianceStatusReportResponse(file, value.TotalCount, value.IsTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied, request, ex.Message, cancellationToken);
            return Result.Failure<ExportGovernanceComplianceStatusReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
