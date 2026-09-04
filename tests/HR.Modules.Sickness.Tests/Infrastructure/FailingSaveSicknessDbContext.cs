using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests.Infrastructure;

/// <summary>
/// OBT-REM-10: test double that lets a test inject a targeted <c>SaveChangesAsync</c> failure
/// (based on whatever the caller decides to inspect on the change tracker) without relying on the
/// iteration order of the entities being saved. Used to prove that a save failure for one entity in
/// a batch does not prevent sibling entities in the same batch from being transitioned and persisted
/// (the regression this ticket fixed by replacing <c>ChangeTracker.Clear()</c> with a per-entity
/// detach).
/// </summary>
internal sealed class FailingSaveSicknessDbContext(
    DbContextOptions<SicknessDbContext> options,
    Func<SicknessDbContext, bool> shouldFail)
    : SicknessDbContext(options)
{
    public int SaveChangesCallCount { get; private set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;

        if (shouldFail(this))
        {
            throw new InvalidOperationException("simulated SaveChangesAsync failure");
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
