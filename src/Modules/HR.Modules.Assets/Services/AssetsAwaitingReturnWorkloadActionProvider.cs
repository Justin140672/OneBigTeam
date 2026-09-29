using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Assets.Services;

internal sealed class AssetsAwaitingReturnWorkloadActionProvider(
    IAssetAssignmentReportReader assetAssignmentReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Assets Awaiting Return";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        if (requestedScope != WorkloadScope.Hr)
            return [];

        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var items = await assetAssignmentReportReader.GetAssetAssignmentsAsync(companyId, cancellationToken);

        var unreturned = items.Where(i => i.ReturnStatus == "Assigned").ToList();
        if (unreturned.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, unreturned.Select(i => i.EmployeeId), cancellationToken);

        return unreturned.Select(item =>
        {
            departments.TryGetValue(item.EmployeeId, out var dept);

            return new WorkloadAction(
                EmployeeId: item.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: $"Return {item.AssetName}",
                ActionCategory: ActionCategory,
                DueDate: null,
                AssignedTo: null,
                Status: "Assigned - Not Yet Returned",
                DeepLinkUrl: $"/companies/{companyId}/employees/{item.EmployeeId}?tab=assets");
        }).ToList();
    }
}
