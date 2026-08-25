using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Reporting.ReportRegistry;
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
            return Result.Success(new GetAssetAssignmentReportResponse([], 0, false));

        var totalCount = items.Count;
        var isTruncated = totalCount > ReportLimits.DisplayRowLimit;
        var cappedItems = items.Take(ReportLimits.DisplayRowLimit).ToList();

        var employeeIds = cappedItems.Select(i => i.EmployeeId).ToHashSet();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var rows = cappedItems
            .Select(item => new AssetAssignmentReportRow(
                item.EmployeeId,
                departments.TryGetValue(item.EmployeeId, out var d) ? d.EmployeeName : item.EmployeeId.ToString(),
                item.AssetName,
                item.SerialNumber,
                item.AssignedDate,
                item.ReturnStatus))
            .ToList();

        return Result.Success(new GetAssetAssignmentReportResponse(rows, totalCount, isTruncated));
    }
}
