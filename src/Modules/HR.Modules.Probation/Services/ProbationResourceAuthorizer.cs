using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Probation.Services;

/// <summary>
/// Resource-level (manager-hierarchy / HR administrator) authorization for Probation endpoints
/// guarded by the "probation:review" policy. That policy only proves the caller holds the
/// Manager or HrAdministrator role — it never proves the caller has a reporting relationship to
/// the specific employee(s) whose probation review data is being requested, so that check lives
/// here and is applied per endpoint before/around the handler call. Standardised on the shared
/// IAM-07 evaluation order by HR.SharedKernel.Authorization.EmployeeResourceAuthorizer. Mirrors
/// HR.Modules.Leave.Services.LeaveResourceAuthorizer / HR.Modules.Sickness.Services
/// .SicknessResourceAuthorizer (PROB-02, following the LEAVE-02/SICK-02 established pattern and
/// its "complete reporting hierarchy" scope decision).
/// </summary>
internal sealed class ProbationResourceAuthorizer
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Probation cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as HR.Modules.Leave.Services.LeaveResourceAuthorizer
    // and HR.Modules.Tasks.Services.TasksResourceAuthorizer's copy. "probation:manage" is a
    // role-based policy (HrAdministrator only), not a permission-based one, so this mirrors
    // LeaveResourceAuthorizer's GetEffectiveRolesAsync check rather than Sickness's
    // permission-id check.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private readonly IAuthorizationService _authorizationService;
    private readonly IDirectReportsReader _directReportsReader;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public ProbationResourceAuthorizer(
        IAuthorizationService authorizationService,
        IDirectReportsReader directReportsReader)
    {
        _authorizationService = authorizationService;
        _directReportsReader = directReportsReader;
        _resourceAuthorizer = new EmployeeResourceAuthorizer(
            IsHrAdministratorAsync,
            directReportsReader.GetAllDescendantIdsAsync);
    }

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => (await _authorizationService.GetEffectiveRolesAsync(callerEmployeeId, cancellationToken))
            .Contains(HrAdministratorRoleId);

    /// <summary>
    /// Resolves the set of employee ids the caller may view probation review data for. Returns
    /// null when the caller is an HR Administrator, meaning access is unrestricted
    /// (company-wide) — callers should skip employee-id filtering entirely in that case rather
    /// than materialising the whole company as a set.
    /// </summary>
    public async Task<IReadOnlySet<Guid>?> GetAuthorizedEmployeeIdsAsync(
        Guid companyId, Guid callerEmployeeId, CancellationToken cancellationToken)
    {
        if (await IsHrAdministratorAsync(callerEmployeeId, cancellationToken))
            return null;

        var descendantIds = await _directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.ToHashSet();
    }

    /// <summary>
    /// Single-resource authorization: HR Administrator, or a manager anywhere above the target
    /// employee in the full reporting hierarchy (direct or indirect), may view. Used for
    /// individual probation review reads, where the review id is a route value that must not be
    /// guessable/enumerable by an unrelated manager. Self-access is deliberately excluded here —
    /// these endpoints are manager/HR-review views, not self-service.
    /// </summary>
    public Task<bool> CanViewEmployeeAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
