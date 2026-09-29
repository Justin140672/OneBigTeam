using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Leave.Services;

internal sealed class LeaveResourceAuthorizer
{
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

    public Task<bool> CanActOnOwnLeaveAsync(
        Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            Guid.Empty, Guid.Empty,
            callerEmployeeId, targetEmployeeId, cancellationToken, allowHierarchy: false);

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

    /// <summary>
    /// Award TOIL actions: HR Administrator, or a manager anywhere above the target employee in
    /// the reporting hierarchy (direct or indirect). Self-award is not a supported path here
    /// (per Ticket 4, Scope TOIL awards to reports and derive actor from authenticated user).
    /// </summary>
    public Task<bool> CanAwardToilAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
