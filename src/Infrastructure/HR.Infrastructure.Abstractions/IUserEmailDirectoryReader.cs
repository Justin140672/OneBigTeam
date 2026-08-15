namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Resolves identity.user_profiles email addresses for a set of Supabase auth user ids (the same
/// id space as IAuditEvent.ActorUserId — see HttpContextCurrentUser.UserId, which reads the JWT
/// "sub" claim), without HR.Modules.Companies taking a direct reference to HR.Modules.Identity
/// (which owns the UserProfile aggregate). Same "Infrastructure.Abstractions port implemented by
/// Identity, consumed by Companies" shape as ICompanyUserEmailSearchReader — used by the Platform
/// Audit Log (GetAuditLog) to display which administrator performed a given action and to support
/// filtering the log by administrator email.
/// </summary>
public interface IUserEmailDirectoryReader
{
    Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(
        IReadOnlyCollection<Guid> supabaseAuthUserIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Supabase auth user id (identity.user_profiles.supabase_auth_user_id) whose
    /// email matches (contains, case-insensitive) the given search term. Used to translate an
    /// "administrator" filter typed as an email into the set of ActorUserId values to filter the
    /// audit log by.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> FindUserIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken);
}
