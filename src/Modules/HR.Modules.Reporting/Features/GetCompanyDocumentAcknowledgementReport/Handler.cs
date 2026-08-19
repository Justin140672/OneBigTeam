using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;

internal sealed class GetCompanyDocumentAcknowledgementReportHandler(
    ICompanyDocumentAcknowledgementReportReader acknowledgementReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader)
{
    public async Task<Result<GetCompanyDocumentAcknowledgementReportResponse>> HandleAsync(
        GetCompanyDocumentAcknowledgementReportRequest request,
        CancellationToken cancellationToken)
    {
        var items = await acknowledgementReportReader.GetAcknowledgementReportAsync(request.CompanyId, cancellationToken);

        if (items.Count == 0)
            return Result.Success(new GetCompanyDocumentAcknowledgementReportResponse([], 0, 0, 0));

        var employeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var rows = items
            .Select(i => new CompanyDocumentAcknowledgementReportRow(
                i.DocumentTitle,
                i.EmployeeId,
                departments.TryGetValue(i.EmployeeId, out var d) ? d.EmployeeName : i.EmployeeId.ToString(),
                i.Acknowledged,
                i.AcknowledgedAt))
            .ToList();

        var totalAcknowledged = rows.Count(r => r.Acknowledged);

        return Result.Success(new GetCompanyDocumentAcknowledgementReportResponse(
            rows, rows.Count, totalAcknowledged, rows.Count - totalAcknowledged));
    }
}
