using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetDocumentComplianceReport;

internal sealed class GetDocumentComplianceReportHandler(
    IDocumentComplianceReportReader documentComplianceReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader)
{
    public async Task<Result<GetDocumentComplianceReportResponse>> HandleAsync(
        GetDocumentComplianceReportRequest request,
        CancellationToken cancellationToken)
    {
        var items = await documentComplianceReportReader.GetDocumentComplianceReportAsync(
            request.CompanyId, request.PositionProfileId, cancellationToken);

        if (items.Count == 0)
            return Result.Success(new GetDocumentComplianceReportResponse([], 0, 0, 0, 0, false));

        // Summary totals (REP-05) are computed from the full filtered set below, before the
        // display cap is applied to the returned rows.
        var totalCount = items.Count;
        var isTruncated = totalCount > ReportLimits.DisplayRowLimit;
        var cappedItems = items.Take(ReportLimits.DisplayRowLimit).ToList();

        var employeeIds = cappedItems.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var rows = cappedItems
            .Select(i => new DocumentComplianceReportRow(
                i.EmployeeId,
                departments.TryGetValue(i.EmployeeId, out var d) ? d.EmployeeName : i.EmployeeId.ToString(),
                i.RequiredCount,
                i.UploadedCount,
                i.MissingCount,
                i.ExpiringSoonCount,
                i.ExpiredCount,
                i.MissingDocumentTypeNames))
            .ToList();

        return Result.Success(new GetDocumentComplianceReportResponse(
            rows,
            totalCount,
            items.Sum(i => i.MissingCount),
            items.Sum(i => i.ExpiringSoonCount),
            items.Sum(i => i.ExpiredCount),
            isTruncated));
    }
}
