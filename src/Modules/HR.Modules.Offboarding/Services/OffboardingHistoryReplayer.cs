using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Services;

// Historical replay counterpart to StartOffboardingHandler: that handler publishes
// OffboardingStartedIntegrationEvent unconditionally whenever an OffboardingPlan is created and
// started (Plan.Create + Plan.Start happen together in the same request, both stamped with the
// same `now`, before the event is published) — regardless of the plan's eventual status. This
// replayer targets exactly the same source — every existing OffboardingPlan row for the company,
// in any status — for offboardings that were started before the employee timeline feature
// existed.
internal sealed class OffboardingHistoryReplayer(
    OffboardingDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher) : IOffboardingHistoryReplayer
{
    public async Task<int> ReplayStartedOffboardingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var plans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        foreach (var plan in plans)
        {
            // CreatedAt is the `now` captured at OffboardingPlan.Create — the same moment the live
            // handler passes as OccurredAt when it publishes the event immediately after creating
            // and starting the plan. An exact match, not a fallback.
            await integrationEventPublisher.PublishAsync(
                new OffboardingStartedIntegrationEvent(plan.CompanyId, plan.EmployeeId, plan.CreatedAt),
                cancellationToken);
        }

        return plans.Count;
    }
}
