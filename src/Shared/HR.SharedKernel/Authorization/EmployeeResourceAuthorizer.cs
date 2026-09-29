namespace HR.SharedKernel.Authorization;

public sealed class EmployeeResourceAuthorizer(
    Func<Guid, CancellationToken, Task<bool>> hasCompanyWideAccessAsync,
    Func<Guid, Guid, CancellationToken, Task<IReadOnlyList<Guid>>> getAllDescendantIdsAsync)
{
    public Task<bool> HasCompanyWideAccessAsync(Guid callerEmployeeId, CancellationToken cancellationToken) =>
        hasCompanyWideAccessAsync(callerEmployeeId, cancellationToken);

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
        if (callerCompanyId != targetCompanyId)
            return false;

        if (await hasCompanyWideAccessAsync(callerEmployeeId, cancellationToken))
            return true;

        if (allowHierarchy)
        {
            var descendantIds = await getAllDescendantIdsAsync(callerCompanyId, callerEmployeeId, cancellationToken);
            if (descendantIds.Contains(targetEmployeeId))
                return true;
        }

        return allowSelf && callerEmployeeId == targetEmployeeId;
    }
}
