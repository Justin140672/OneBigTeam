using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.GovernanceReporting;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;

/// <summary>
/// ADM-08 User Activity governance report. Surfaces what users did across the company from the
/// central audit source (<see cref="IAuditHistoryReader"/>), never a competing record. Company
/// isolation is enforced by the reader; authorization is the endpoint's
/// "reporting:view" + "reporting:view-governance" policies.
/// </summary>
internal sealed class GetGovernanceUserActivityReportHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    private const GovernanceAuditScope Scope = GovernanceAuditScope.UserActivity;

    public async Task<Result<GetGovernanceUserActivityReportResponse>> HandleAsync(
        GetGovernanceUserActivityReportRequest request,
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

        return Result.Success(new GetGovernanceUserActivityReportResponse(
            pageItems, totalCount, request.Page, request.PageSize, isTruncated));
    }
}
