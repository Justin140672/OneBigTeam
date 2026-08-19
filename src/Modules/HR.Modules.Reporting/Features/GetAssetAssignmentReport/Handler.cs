using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetAssetAssignmentReport;

internal sealed class GetAssetAssignmentReportHandler(
    IAssetAssignmentReportReader assetAssignmentReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader)
{
    public async Task<Result<GetAssetAssignmentReportResponse>> HandleAsync(
        GetAssetAssignmentReportRequest request,
        CancellationToken cancellationToken)
    {
        var items = await assetAssignmentReportReader.GetAssetAssignmentsAsync(request.CompanyId, cancellationToken);

        if (items.Count == 0)
            return Result.Success(new GetAssetAssignmentReportResponse([], 0));

        var employeeIds = items.Select(i => i.EmployeeId).ToHashSet();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var rows = items
            .Select(item => new AssetAssignmentReportRow(
                item.EmployeeId,
                departments.TryGetValue(item.EmployeeId, out var d) ? d.EmployeeName : item.EmployeeId.ToString(),
                item.AssetName,
                item.SerialNumber,
                item.AssignedDate,
                item.ReturnStatus))
            .ToList();

        return Result.Success(new GetAssetAssignmentReportResponse(rows, rows.Count));
    }
}
