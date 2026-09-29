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
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Assets cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as LeaveResourceAuthorizer.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    /// <summary>Checks whether the caller holds the HR Administrator role.</summary>
    public async Task<bool> IsHrAdministratorAsync(Guid callerUserId, CancellationToken cancellationToken)
        => (await authorizationService.GetEffectiveRolesAsync(callerUserId, cancellationToken))
            .Contains(HrAdministratorRoleId);

    /// <summary>
    /// Checks if a caller can view assets assigned to a specific employee.
    /// Authorization rule: self, direct manager (not full hierarchy), or HR Administrator.
    /// </summary>
    public async Task<bool> CanViewEmployeeAssetsAsync(
        Guid companyId,
        Guid callerUserId,
        Guid targetEmployeeId,
        CancellationToken cancellationToken)
    {
        // 1. Check if caller is HR admin
        var effectiveRoles = await authorizationService.GetEffectiveRolesAsync(callerUserId, cancellationToken);
        if (effectiveRoles.Contains(HrAdministratorRoleId))
            return true;

        // 2. Check if caller is the target employee (self-service)
        if (callerUserId == targetEmployeeId)
            return true;

        // 3. Check if caller is the direct manager of the target employee (direct only)
        var managerId = await managerReader.GetManagerIdAsync(companyId, targetEmployeeId, cancellationToken);
        if (managerId == callerUserId)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a caller can view a specific asset assignment.
    /// Authorization rule: self, direct manager (not full hierarchy), or HR Administrator.
    /// </summary>
    public async Task<bool> CanViewAssetAssignmentAsync(
        Guid companyId,
        Guid callerUserId,
        Guid assignedEmployeeId,
        CancellationToken cancellationToken)
        => await CanViewEmployeeAssetsAsync(companyId, callerUserId, assignedEmployeeId, cancellationToken);
}
