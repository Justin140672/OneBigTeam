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
    /// View the full employee record: the employee themself, any manager in their full reporting
    /// hierarchy, or an HR Administrator.
    /// </summary>
    public Task<bool> CanViewAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken);
}
