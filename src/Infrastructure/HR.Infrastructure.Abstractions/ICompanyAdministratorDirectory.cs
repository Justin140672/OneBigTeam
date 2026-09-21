namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface for resolving which employees hold the Company Administrator role for
/// a company — mirrors <see cref="IHrAdministratorDirectory"/>. Used by modules that need to reach
/// a company's business owner(s) without referencing HR.Modules.Identity directly (e.g. Customer
/// Release Notifications' SendProductUpdate, which notifies every active customer's Company
/// Administrators). Implemented in the Identity module (the schema owner for roles/user profiles)
/// and DI-registered there.
/// </summary>
public interface ICompanyAdministratorDirectory
{
    /// <summary>
    /// Employee ids (== UserProfile ids, by this codebase's established employee/user id
    /// convention) of every active (UserProfile.IsActive == true — excludes disabled/revoked
    /// accounts) Company Administrator for the given company.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveCompanyAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
