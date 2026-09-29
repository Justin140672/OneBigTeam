using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests.Infrastructure;

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
