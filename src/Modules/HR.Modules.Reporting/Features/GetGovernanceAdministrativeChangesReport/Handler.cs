using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.GovernanceReporting;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;

/// <summary>
/// ADM-08 Administrative Changes governance report. Reads the central audit source
/// (<see cref="IAuditHistoryReader"/>) and narrows to configuration / settings / role / policy
/// change events. Company isolation is enforced by the reader.
/// </summary>
internal sealed class GetGovernanceAdministrativeChangesReportHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    private const GovernanceAuditScope Scope = GovernanceAuditScope.AdministrativeChanges;

    public async Task<Result<GetGovernanceAdministrativeChangesReportResponse>> HandleAsync(
        GetGovernanceAdministrativeChangesReportRequest request,
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

        return Result.Success(new GetGovernanceAdministrativeChangesReportResponse(
            pageItems, totalCount, request.Page, request.PageSize, isTruncated));
    }
}
