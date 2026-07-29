using HR.Infrastructure.Abstractions;
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
            return Result.Success(new GetDocumentComplianceReportResponse([], 0, 0, 0, 0));

        var employeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var rows = items
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
            rows.Count,
            rows.Sum(r => r.MissingCount),
            rows.Sum(r => r.ExpiringSoonCount),
            rows.Sum(r => r.ExpiredCount)));
    }
}
