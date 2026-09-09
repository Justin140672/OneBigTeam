using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 (optimistic concurrency) base code. Centralises the "every change to a versioned
/// record advances its <see cref="IVersionedAggregate.Version"/>" rule so that writers which do
/// not go through <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/>
/// (e.g. AssignManager, PromoteEmployee, background jobs) still bump the token.
///
/// It is idempotent-safe: if the <c>Version</c> property has already been marked modified for an
/// entry (which is what the save helper's explicit <see cref="IVersionedAggregate.IncrementVersion"/>
/// call does) the interceptor leaves it alone, so there is never a double increment.
/// </summary>
public sealed class VersionAdvancingSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly VersionAdvancingSaveChangesInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AdvanceVersions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AdvanceVersions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void AdvanceVersions(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<IVersionedAggregate>())
        {
            if (entry.State != EntityState.Modified)
                continue;

            var versionProperty = entry.Property(nameof(IVersionedAggregate.Version));
            if (versionProperty.IsModified)
                continue;

            entry.Entity.IncrementVersion();
        }
    }
}
