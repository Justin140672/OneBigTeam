using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Resource-level (self / manager-hierarchy / HR administrator) authorization for Leave
/// endpoints that accept an employeeId route value. Endpoint-level Policies(...) only prove
/// tenant/role membership — they never prove the caller has a relationship to the specific
/// employeeId in the route, so that check lives here and is applied per endpoint before the
/// handler runs. Standardised on the shared IAM-07 evaluation order by
/// HR.SharedKernel.Authorization.EmployeeResourceAuthorizer. See
/// HR.Modules.Tasks.Services.TasksResourceAuthorizer for the pattern this mirrors (SEC-003).
/// </summary>
internal sealed class LeaveResourceAuthorizer
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Leave cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as GetRecentLeaveRequests/Endpoint.cs's
    // HrAdministratorRoleId and HR.Modules.Tasks.Services.TasksResourceAuthorizer's copy.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private readonly IAuthorizationService _authorizationService;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public LeaveResourceAuthorizer(
        IAuthorizationService authorizationService,
        IDirectReportsReader directReportsReader)
    {
        _authorizationService = authorizationService;
        _resourceAuthorizer = new EmployeeResourceAuthorizer(
            IsHrAdministratorAsync,
            directReportsReader.GetAllDescendantIdsAsync);
    }

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => (await _authorizationService.GetEffectiveRolesAsync(callerEmployeeId, cancellationToken))
            .Contains(HrAdministratorRoleId);

    /// <summary>
    /// Self-service actions only: Submit, Preview, Cancel. Per LEAVE-01, managers do not act on
    /// leave "as" someone else — only the employee themself or an HR Administrator may. No
    /// company-boundary argument is needed here (unlike the other methods below): the caller's
    /// own company is irrelevant to a purely self/HR-admin decision, so a fixed, matching pair
    /// is passed to satisfy EmployeeResourceAuthorizer's signature without asserting anything
    /// about tenancy — the endpoint's own tenant check already covers that.
    /// </summary>
    public Task<bool> CanActOnOwnLeaveAsync(
        Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            Guid.Empty, Guid.Empty,
            callerEmployeeId, targetEmployeeId, cancellationToken, allowHierarchy: false);

    /// <summary>
    /// View/read actions: Get, List, GetEmployeeLeaveBalance, GetLeaveBalanceHistory. The
    /// employee themself, any manager in their full reporting hierarchy, and HR Administrators
    /// may view.
    /// </summary>
    public Task<bool> CanViewAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken);

    /// <summary>
    /// Approve/Reject actions: HR Administrator, or a manager anywhere above the target
    /// employee in the reporting hierarchy (direct or indirect). Self-approval is not a
    /// supported path here.
    /// </summary>
    public Task<bool> CanApproveOrRejectAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
