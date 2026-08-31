using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.GovernanceReporting;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;

/// <summary>
/// ADM-08 Security Events governance report. Reads the central audit source
/// (<see cref="IAuditHistoryReader"/>) and narrows to authentication / permission / account-status
/// / role-assignment events. Company isolation is enforced by the reader.
/// </summary>
internal sealed class GetGovernanceSecurityEventsReportHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    private const GovernanceAuditScope Scope = GovernanceAuditScope.SecurityEvents;

    public async Task<Result<GetGovernanceSecurityEventsReportResponse>> HandleAsync(
        GetGovernanceSecurityEventsReportRequest request,
        CancellationToken cancellationToken)
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

        var pageItems = rows
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return Result.Success(new GetGovernanceSecurityEventsReportResponse(
            pageItems, totalCount, request.Page, request.PageSize, isTruncated));
    }
}
