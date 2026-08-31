using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;

/// <summary>
/// ADM-08 Compliance Status governance report. Delegates entirely to the ADM-02
/// <see cref="GetComplianceCentreHandler"/> composition (expiring documents, missing required
/// documents, outstanding document requests, probation reviews) and re-shapes the result as a paged
/// report row set — so there is exactly one compliance data source, not a competing one.
/// </summary>
internal sealed class GetGovernanceComplianceStatusReportHandler(GetComplianceCentreHandler complianceCentreHandler)
{
    public async Task<Result<GetGovernanceComplianceStatusReportResponse>> HandleAsync(
        GetGovernanceComplianceStatusReportRequest request,
        CancellationToken cancellationToken)
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
            .Select(i => new ComplianceStatusRow(
                i.EmployeeId, i.EmployeeName, i.Department, i.Category, i.CategoryLabel, i.Detail, i.DueDate, i.Severity))
            .ToList();

        var pageItems = rows
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return Result.Success(new GetGovernanceComplianceStatusReportResponse(
            pageItems, value.TotalCount, request.Page, request.PageSize, value.IsTruncated));
    }
}
