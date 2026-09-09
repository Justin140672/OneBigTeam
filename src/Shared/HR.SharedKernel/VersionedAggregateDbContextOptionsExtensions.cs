using Microsoft.EntityFrameworkCore;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 (optimistic concurrency) base code. One-liner used by every module's
/// <c>AddDbContext</c> registration and design-time factory to install the shared
/// <see cref="VersionAdvancingSaveChangesInterceptor"/> so version advance is enforced
/// consistently across all module DbContexts.
/// </summary>
public static class VersionedAggregateDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UseVersionedAggregates(this DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddInterceptors(VersionAdvancingSaveChangesInterceptor.Instance);
    }
}
