using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Resource-level (self / manager-hierarchy / HR administrator) authorization for Leave
/// endpoints that accept an employeeId route value. Endpoint-level Policies(...) only prove
/// tenant/role membership — they never prove the caller has a relationship to the specific
/// employeeId in the route, so that check lives here and is applied per endpoint before the
/// handler runs. See HR.Modules.Tasks.Features.CompleteTask.Handler for the pattern this
/// mirrors (SEC-003).
/// </summary>
internal sealed class LeaveResourceAuthorizer(
    IAuthorizationService authorizationService,
    IDirectReportsReader directReportsReader)
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Leave cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as GetRecentLeaveRequests/Endpoint.cs's
    // HrAdministratorRoleId and HR.Modules.Tasks.Features.CompleteTask.Handler's copy.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => (await authorizationService.GetEffectiveRolesAsync(callerEmployeeId, cancellationToken))
            .Contains(HrAdministratorRoleId);

    /// <summary>
    /// Self-service actions only: Submit, Preview, Cancel. Per LEAVE-01, managers do not act on
    /// leave "as" someone else — only the employee themself or an HR Administrator may.
    /// </summary>
    public async Task<bool> CanActOnOwnLeaveAsync(
        Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (callerEmployeeId == targetEmployeeId)
            return true;

        return await IsHrAdministratorAsync(callerEmployeeId, cancellationToken);
    }

    /// <summary>
    /// View/read actions: Get, List, GetEmployeeLeaveBalance, GetLeaveBalanceHistory. The
    /// employee themself, any manager in their full reporting hierarchy, and HR Administrators
    /// may view.
    /// </summary>
    public async Task<bool> CanViewAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (callerEmployeeId == targetEmployeeId)
            return true;

        if (await IsHrAdministratorAsync(callerEmployeeId, cancellationToken))
            return true;

        var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.Contains(targetEmployeeId);
    }

    /// <summary>
    /// Approve/Reject actions: HR Administrator, or a manager anywhere above the target
    /// employee in the reporting hierarchy (direct or indirect). Self-approval is not a
    /// supported path here.
    /// </summary>
    public async Task<bool> CanApproveOrRejectAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (await IsHrAdministratorAsync(callerEmployeeId, cancellationToken))
            return true;

        var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.Contains(targetEmployeeId);
    }
}
