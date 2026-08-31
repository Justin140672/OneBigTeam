using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Onboarding.Services;

/// <summary>
/// DSH-02: resource-level (self / manager-hierarchy / HR administrator) authorization for the
/// team onboarding dashboard endpoint. The "role:employee" policy on GetTeamOnboarding only
/// proves tenant membership — it never proves the caller has a reporting relationship to the
/// browser-supplied {managerId} route value, so that check lives here. Mirrors
/// HR.Modules.Probation.Services.ProbationResourceAuthorizer /
/// HR.Modules.Sickness.Services.SicknessResourceAuthorizer, standardised on the shared IAM-07
/// evaluation order via HR.SharedKernel.Authorization.EmployeeResourceAuthorizer.
/// </summary>
internal sealed class OnboardingResourceAuthorizer
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Onboarding cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as ProbationResourceAuthorizer.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private readonly IAuthorizationService _authorizationService;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public OnboardingResourceAuthorizer(
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
    /// The caller may view the team onboarding of <paramref name="managerId"/> only if they ARE
    /// that manager, sit ABOVE them in the reporting hierarchy, or hold company-wide (HR
    /// administrator) access. See specifications/architecture/11-manager-hierarchy-scope.md.
    /// </summary>
    public Task<bool> CanViewManagerTeamAsync(
        Guid companyId, Guid callerEmployeeId, Guid managerId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, managerId, cancellationToken);
}
