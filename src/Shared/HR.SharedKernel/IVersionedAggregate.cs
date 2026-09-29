namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 (optimistic concurrency) base-code contract. An aggregate root that carries an
/// application-managed, persisted integer concurrency token — mapped with
/// <c>.IsConcurrencyToken()</c> in the module's EF configuration, matching the
/// <c>CompanySettings.Version</c> convention rather than a provider-generated row version.
///
/// User-facing edit handlers call
/// <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/> which pins the
/// EF <c>OriginalValue</c> of <see cref="Version"/> to the value the client loaded and increments
/// it before saving. A stale save then affects zero rows and is translated to a
/// <see cref="Error.Concurrency(string)"/> failure (HTTP 409).
/// </summary>
public interface IVersionedAggregate
{
    int Version { get; }

    void IncrementVersion();
}
