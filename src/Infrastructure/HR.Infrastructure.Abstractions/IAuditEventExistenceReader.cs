namespace HR.Infrastructure.Abstractions;

/// <summary>
/// OBT-REM-12: lets a module check whether a committed audit event already exists for a given
/// (deterministic) <c>EventId</c> before deciding to republish it during bounded reconciliation.
/// Audit persistence is an Infrastructure concern (see 02-module-boundaries.md); modules must not
/// reference the audit schema/DbContext directly, so this narrow read-only contract is exposed
/// through Infrastructure.Abstractions and implemented in HR.Infrastructure, mirroring every other
/// I*Reader bridge already used for cross-cutting reads (e.g. IUserEmailReader,
/// ICompanyNotificationSettingsReader).
/// </summary>
public interface IAuditEventExistenceReader
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);
}
