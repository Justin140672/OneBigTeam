namespace HR.SharedKernel.Authorization;

/// <summary>
/// IAM-07: standard resource-level authorizer for employee-owned resources (documents, tasks,
/// sickness records, probation reviews, leave requests, and similar per-employee data).
///
/// Encapsulates the mandated evaluation order for "can caller access target employee's
/// resource":
///   1. Company boundary  — caller and target must belong to the same company. Endpoint-level
///      tenant checks (TenantRouteAuthorizationMiddleware) already guarantee the caller's own
///      company matches the route company, so this class re-asserts that the target employee
///      resolves within that same company (callers pass the already-validated companyId for
///      both sides).
///   2. Permission          — does the caller hold a company-wide grant (e.g. HR Administrator)?
///      If so, access is company-wide and no further checks are needed.
///   3. Scope / hierarchy   — does the caller manage the target anywhere in the complete
///      reporting hierarchy (direct or indirect report)?
///   4. Resource ownership  — is the caller the target employee themself (self-service access)?
///
/// A module constructs one instance per resource family, supplying its own permission/role
/// check (via <see cref="IAuthorizationService"/>, already available here) and its own
/// hierarchy resolver (each module reaches this through its own IDirectReportsReader-shaped
/// contract — see HR.Modules.Employees.Contracts.IDirectReportsReader — so this class never
/// takes a dependency on that module-owned contract, keeping SharedKernel free of module
/// references). This is the single place the "self / hierarchy / company-wide" decision logic
/// lives, replacing the near-identical logic that used to be duplicated across
/// DocumentResourceAuthorizer, SicknessResourceAuthorizer, LeaveResourceAuthorizer,
/// ProbationResourceAuthorizer and CompleteTaskHandler.
/// </summary>
public sealed class EmployeeResourceAuthorizer(
    Func<Guid, CancellationToken, Task<bool>> hasCompanyWideAccessAsync,
    Func<Guid, Guid, CancellationToken, Task<IReadOnlyList<Guid>>> getAllDescendantIdsAsync)
{
    /// <summary>
    /// Company-wide access check alone (e.g. "is this caller an HR Administrator"), exposed so
    /// callers that need it standalone (list/search endpoints scoping a whole result set) don't
    /// need to duplicate the permission lookup.
    /// </summary>
    public Task<bool> HasCompanyWideAccessAsync(Guid callerEmployeeId, CancellationToken cancellationToken) =>
        hasCompanyWideAccessAsync(callerEmployeeId, cancellationToken);

    /// <summary>
    /// Resolves every descendant (direct or indirect report) of <paramref name="callerEmployeeId"/>
    /// within <paramref name="companyId"/>, exposed so callers building an allow-list for a
    /// list/search endpoint don't need a second hierarchy dependency of their own.
    /// </summary>
    public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(
        Guid companyId, Guid callerEmployeeId, CancellationToken cancellationToken) =>
        getAllDescendantIdsAsync(companyId, callerEmployeeId, cancellationToken);

    /// <summary>
    /// Single-resource authorization for a specific target employee's resource: evaluates
    /// company boundary, company-wide permission, hierarchy, and self-ownership in that order.
    /// </summary>
    /// <param name="callerCompanyId">The caller's own, already-tenant-validated company id.</param>
    /// <param name="targetCompanyId">The company id the target employee/resource belongs to.</param>
    /// <param name="callerEmployeeId">The caller's own employee id.</param>
    /// <param name="targetEmployeeId">The employee id that owns the resource being accessed.</param>
    /// <param name="allowSelf">Whether the target employee themself may access this resource (some
    /// actions, e.g. manager-only approvals, are deliberately not self-serviceable).</param>
    /// <param name="allowHierarchy">Whether a manager anywhere in the target's reporting
    /// hierarchy may access this resource (some actions, e.g. leave submission, are
    /// self+HR-admin only).</param>
    public async Task<bool> CanAccessAsync(
        Guid callerCompanyId,
        Guid targetCompanyId,
        Guid callerEmployeeId,
        Guid targetEmployeeId,
        CancellationToken cancellationToken,
        bool allowSelf = true,
        bool allowHierarchy = true)
    {
        // 1. Company boundary.
        if (callerCompanyId != targetCompanyId)
            return false;

        // 2. Permission — company-wide grant (e.g. HR Administrator).
        if (await hasCompanyWideAccessAsync(callerEmployeeId, cancellationToken))
            return true;

        // 3. Scope / hierarchy — manager anywhere above the target in the full reporting tree.
        if (allowHierarchy)
        {
            var descendantIds = await getAllDescendantIdsAsync(callerCompanyId, callerEmployeeId, cancellationToken);
            if (descendantIds.Contains(targetEmployeeId))
                return true;
        }

        // 4. Resource ownership — the caller is the target employee themself.
        return allowSelf && callerEmployeeId == targetEmployeeId;
    }
}
