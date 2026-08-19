namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Resolves how many identity.user_profiles rows (portal/login accounts) exist for a company,
/// without HR.Modules.Companies taking a direct reference to HR.Modules.Identity (which owns the
/// UserProfile aggregate) — same cross-module pattern as ICompanyUserEmailSearchReader.
/// Deliberately distinct from an employee count: a UserProfile is a login-capable portal account,
/// while an employee is an HR record — the two are not 1:1 (an employee may have no portal
/// account yet, and a platform-admin persona could in principle have no employee record at all).
/// Implemented in HR.Modules.Identity, consumed by HR.Modules.Companies' Customer Support View.
/// Despite the "Company" prefix (reflecting the consumer), this contract is owned by
/// HR.Modules.Identity (the implementer), so it is not part of HR.Modules.Companies.Contracts —
/// moving it there would misrepresent ownership. Left here pending an Identity.Contracts project.
/// </summary>
public interface ICompanyUserCountReader
{
    Task<int> GetUserCountAsync(Guid companyId, CancellationToken cancellationToken);
}
