namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Resolves which companies have at least one identity.user_profiles row whose email matches a
/// search term, without HR.Modules.Companies taking a direct reference to HR.Modules.Identity
/// (which owns the UserProfile aggregate). Implemented in HR.Modules.Identity and consumed by
/// HR.Modules.Companies' platform-admin customer list (ListCustomers), whose "search by email"
/// criterion needs to match against user emails that live outside the companies schema entirely.
/// </summary>
public interface ICompanyUserEmailSearchReader
{
    Task<IReadOnlyCollection<Guid>> FindCompanyIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken);
}
