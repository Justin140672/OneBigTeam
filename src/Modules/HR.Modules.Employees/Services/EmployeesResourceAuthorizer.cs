using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeesResourceAuthorizer
{
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

    public Task<bool> CanViewAsManagerAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
