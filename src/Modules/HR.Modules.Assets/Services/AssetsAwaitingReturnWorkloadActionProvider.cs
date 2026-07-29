using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Assets.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for assets awaiting return. HR-only, reuses
/// IAssetAssignmentReportReader (already used by GetAssetAssignmentReport/Handler.cs).
///
/// Interpretation note (per OBT-721 ticket guidance to document rather than block when there's no
/// clean 1:1 domain concept, same approach as VacanciesAwaitingActionWorkloadActionProvider):
/// AssetAssignment has no "return requested" flag — RequestAssetReturn only notifies, it does not
/// set a distinguishing field visible on AssetAssignmentReportItem. Every currently-active
/// (unreturned) assignment is therefore treated as "awaiting return" here. DueDate is left null
/// (Upcoming urgency) since there is no return-due-by field either.
/// </summary>
internal sealed class AssetsAwaitingReturnWorkloadActionProvider(
    IAssetAssignmentReportReader assetAssignmentReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Assets Awaiting Return";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
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
                DeepLinkUrl: $"/companies/{companyId}/assets/assignments/{item.AssetAssignmentId}/view");
        }).ToList();
    }
}
