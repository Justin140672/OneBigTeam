using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class SubscriptionStatusReader(CompaniesDbContext dbContext, IClock clock) : ISubscriptionStatusReader
{
    public async Task<SubscriptionStatusSnapshot> GetStatusAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            // No subscription row (e.g. a seeded dev company predating this epic) — treat as
            // trial-expired/read-only rather than throwing, so callers always get a usable snapshot.
            return new SubscriptionStatusSnapshot(SubscriptionStatus.TrialExpired, IsReadOnly: true, TrialDaysRemaining: 0);
        }

        if (subscription.MarkExpiredIfNeeded(now))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var isReadOnly = subscription.Status == SubscriptionStatus.TrialExpired || subscription.AdminForcedReadOnly;
        var trialDaysRemaining = subscription.Status == SubscriptionStatus.Trial
            ? Math.Max(0, (subscription.TrialExpiresAt - now).Days)
            : 0;

        return new SubscriptionStatusSnapshot(subscription.Status, isReadOnly, trialDaysRemaining);
    }
}
