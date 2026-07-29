using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetOffboardingProgressReport;

internal sealed class GetOffboardingProgressReportHandler(
    IOffboardingReportReader offboardingReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IEmployeeUserAccountStatusReader employeeUserAccountStatusReader,
    IAssignedAssetReader assignedAssetReader)
{
    public async Task<Result<GetOffboardingProgressReportResponse>> HandleAsync(
        GetOffboardingProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        var items = await offboardingReportReader.GetOffboardingReportAsync(request.CompanyId, cancellationToken);

        if (items.Count == 0)
            return Result.Success(new GetOffboardingProgressReportResponse([], 0, 0, 0));

        var employeeIds = items.Select(i => i.EmployeeId).ToHashSet();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var accountStatuses = await employeeUserAccountStatusReader.GetStatusesAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var rows = new List<OffboardingProgressReportRow>();
        foreach (var item in items)
        {
            // Per-employee call because IAssignedAssetReader has no bulk overload yet — a future
            // pass could add one if this report is used at scale (see task doc comment).
            var assignedAssets = await assignedAssetReader.GetAssignedAssetsAsync(
                request.CompanyId, item.EmployeeId, cancellationToken);

            var accessDisabled = accountStatuses.TryGetValue(item.EmployeeId, out var summary)
                && summary.Status != EmployeeUserAccountStatus.Active;
            // Employees absent from the returned dictionary have no account at all, i.e. access is
            // already disabled/never existed.
            if (!accountStatuses.ContainsKey(item.EmployeeId))
                accessDisabled = true;

            rows.Add(new OffboardingProgressReportRow(
                item.EmployeeId,
                departments.TryGetValue(item.EmployeeId, out var d) ? d.EmployeeName : item.EmployeeId.ToString(),
                item.LastWorkingDay,
                item.Status,
                item.OutstandingTaskTitles,
                item.CompletedTaskTitles,
                accessDisabled,
                item.DocumentsReturned,
                assignedAssets.Count == 0));
        }

        var outstandingAccessCount = rows.Count(r => !r.AccessDisabled);
        var outstandingAssetsCount = rows.Count(r => !r.AssetsReturned);

        return Result.Success(new GetOffboardingProgressReportResponse(
            rows, rows.Count, outstandingAccessCount, outstandingAssetsCount));
    }
}
