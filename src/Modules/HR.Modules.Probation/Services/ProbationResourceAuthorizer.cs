using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Probation.Services;

/// <summary>
/// Resource-level (manager-hierarchy / HR administrator) authorization for Probation endpoints
/// guarded by the "probation:review" policy. That policy only proves the caller holds the
/// Manager or HrAdministrator role — it never proves the caller has a reporting relationship to
/// the specific employee(s) whose probation review data is being requested, so that check lives
/// here and is applied per endpoint before/around the handler call. Mirrors
/// HR.Modules.Leave.Services.LeaveResourceAuthorizer / HR.Modules.Sickness.Services
/// .SicknessResourceAuthorizer (PROB-02, following the LEAVE-02/SICK-02 established pattern and
/// its "complete reporting hierarchy" scope decision).
/// </summary>
internal sealed class ProbationResourceAuthorizer(
    IAuthorizationService authorizationService,
    IDirectReportsReader directReportsReader)
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Probation cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as HR.Modules.Leave.Services.LeaveResourceAuthorizer
    // and HR.Modules.Tasks.Features.CompleteTask.Handler's copy. "probation:manage" is a
    // role-based policy (HrAdministrator only), not a permission-based one, so this mirrors
    // LeaveResourceAuthorizer's GetEffectiveRolesAsync check rather than Sickness's
    // permission-id check.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => (await authorizationService.GetEffectiveRolesAsync(callerEmployeeId, cancellationToken))
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

        var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.ToHashSet();
    }

    /// <summary>
    /// Single-resource authorization: HR Administrator, or a manager anywhere above the target
    /// employee in the full reporting hierarchy (direct or indirect), may view. Used for
    /// individual probation review reads, where the review id is a route value that must not be
    /// guessable/enumerable by an unrelated manager.
    /// </summary>
    public async Task<bool> CanViewEmployeeAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (await IsHrAdministratorAsync(callerEmployeeId, cancellationToken))
            return true;

        var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.Contains(targetEmployeeId);
    }
}
