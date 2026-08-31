using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.GovernanceReporting;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;

/// <summary>
/// ADM-08. Exports the User Activity governance report through the established reporting export
/// framework (<see cref="IReportExporter"/> + <see cref="ReportExportAuditor"/>). The endpoint
/// applies the same policy pair as the on-screen report and the query applies the same filters, so
/// exported rows always match the on-screen scope. Column set carries no salary / NI / token data.
/// </summary>
internal sealed class ExportGovernanceUserActivityReportHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "governance-user-activity";
    private const GovernanceAuditScope Scope = GovernanceAuditScope.UserActivity;

    public async Task<Result<ExportGovernanceUserActivityReportResponse>> HandleAsync(
        ExportGovernanceUserActivityReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (rows, totalCount, isTruncated) = await GovernanceAuditReportSupport.QueryAsync(
                auditHistoryReader,
                userEmailDirectoryReader,
                Scope,
                request.CompanyId,
                request.ActorUserId,
                request.EventType,
                request.EmployeeId,
                request.FromDate,
                request.ToDate,
                request.Status,
                cancellationToken);

            var exportData = new ReportExportData(
                "Governance — User Activity",
                GovernanceAuditReportSupport.ColumnHeaders,
                rows.Select(GovernanceAuditReportSupport.ToExportRow).ToList());

            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), totalCount,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportGovernanceUserActivityReportResponse(file, totalCount, isTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportGovernanceUserActivityReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
