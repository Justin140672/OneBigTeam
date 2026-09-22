using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Resource-level (self / manager-hierarchy / HR administrator) authorization for Employees
/// endpoints that return a specific employee's full HR record. Endpoint-level Policies(...) only
/// prove tenant/role membership — they never prove the caller has a relationship to the specific
/// employeeId in the route, so that check lives here and is applied per endpoint before the
/// handler builds a response. Standardised on the shared IAM-07 evaluation order by
/// HR.SharedKernel.Authorization.EmployeeResourceAuthorizer. See
/// HR.Modules.Leave.Services.LeaveResourceAuthorizer/HR.Modules.Tasks.Services.TasksResourceAuthorizer
/// for the pattern this mirrors.
/// </summary>
internal sealed class EmployeesResourceAuthorizer
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Employees cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as every other module's *ResourceAuthorizer.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private readonly IAuthorizationService _authorizationService;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public EmployeesResourceAuthorizer(
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
    /// View the full HR employee record (GetEmployee): the employee themself or an HR
    /// Administrator only. A manager in the target's reporting hierarchy is deliberately excluded
    /// here — the full record contains fields (personal contact details, DOB, demographic data,
    /// home address, HR notes, leaving-process detail, notice period, system-access state) a
    /// manager is not approved to see. Manager access goes through the separate, field-restricted
    /// GetEmployeeTeamView endpoint/CanViewAsManagerAsync below instead, backed by its own
    /// operational-only database projection — see 26-permissions-access-ux.md's field-level access
    /// matrix.
    /// </summary>
    public Task<bool> CanViewFullRecordAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowHierarchy: false);

    /// <summary>
    /// View the operational-only manager team-view of an employee record (GetEmployeeTeamView):
    /// any manager anywhere in the target's reporting hierarchy, direct or indirect. Excludes
    /// self-access (an employee views their own record through GetEmployee, never this reduced
    /// view) — HR Administrators still pass here too (the shared authorizer's company-wide check
    /// runs unconditionally), which is harmless: HR simply has no reason to call this endpoint
    /// when GetEmployee already gives them the full record.
    /// </summary>
    public Task<bool> CanViewAsManagerAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
