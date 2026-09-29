using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Assets.Services;

/// <summary>
/// Resource-level (self / direct-manager-only / HR administrator) authorization for Assets
/// endpoints that accept an employeeId route value or query an asset's current assignment.
/// Endpoint-level Policies(...) only prove tenant/role membership — they never prove the
/// caller has a relationship to the specific employeeId in the route or the employee
/// currently assigned to an asset, so that check lives here and is applied per endpoint
/// before the handler runs. Simplified version (Ticket 3): checks direct manager only,
/// not full reporting hierarchy, for a pragmatic integration-test-focused approach.
/// Mirrors HR.Modules.Leave.Services.LeaveResourceAuthorizer pattern.
/// </summary>
internal sealed class AssetResourceAuthorizer(
    IAuthorizationService authorizationService,
    IManagerReader managerReader)
{
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    public async Task<bool> IsHrAdministratorAsync(Guid callerUserId, CancellationToken cancellationToken)
        => (await authorizationService.GetEffectiveRolesAsync(callerUserId, cancellationToken))
            .Contains(HrAdministratorRoleId);

    public async Task<bool> CanViewEmployeeAssetsAsync(
        Guid companyId,
        Guid callerUserId,
        Guid targetEmployeeId,
        CancellationToken cancellationToken)
    {
        var effectiveRoles = await authorizationService.GetEffectiveRolesAsync(callerUserId, cancellationToken);
        if (effectiveRoles.Contains(HrAdministratorRoleId))
            return true;

        if (callerUserId == targetEmployeeId)
            return true;

        var managerId = await managerReader.GetManagerIdAsync(companyId, targetEmployeeId, cancellationToken);
        if (managerId == callerUserId)
            return true;

        return false;
    }

    public async Task<bool> CanViewAssetAssignmentAsync(
        Guid companyId,
        Guid callerUserId,
        Guid assignedEmployeeId,
        CancellationToken cancellationToken)
        => await CanViewEmployeeAssetsAsync(companyId, callerUserId, assignedEmployeeId, cancellationToken);
}
