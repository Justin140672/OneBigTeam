using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.GovernanceReporting;
using HR.Modules.Reporting.Services;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;

internal sealed class ExportGovernanceSecurityEventsReportHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader,
    IReportExporter reportExporter,
    ReportExportAuditor auditor)
{
    private const string ReportId = "governance-security-events";
    private const GovernanceAuditScope Scope = GovernanceAuditScope.SecurityEvents;

    public async Task<Result<ExportGovernanceSecurityEventsReportResponse>> HandleAsync(
        ExportGovernanceSecurityEventsReportRequest request,
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
                "Governance — Security Events",
                GovernanceAuditReportSupport.ColumnHeaders,
                rows.Select(GovernanceAuditReportSupport.ToExportRow).ToList());

            var file = reportExporter.Export(request.Format, exportData);

            await auditor.PublishSuccessAsync(
                request.CompanyId, ReportId, request.Format.ToString(), totalCount,
                managerScopeApplied: false, request, cancellationToken);

            return Result.Success(new ExportGovernanceSecurityEventsReportResponse(file, totalCount, isTruncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditor.PublishFailureAsync(
                request.CompanyId, ReportId, request.Format.ToString(),
                managerScopeApplied: false, request, ex.Message, cancellationToken);
            return Result.Failure<ExportGovernanceSecurityEventsReportResponse>(Error.Unexpected("Report export failed."));
        }
    }
}
