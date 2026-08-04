using HR.Modules.CompanyOnboarding.Domain;
using HR.Modules.CompanyOnboarding.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.CompanyOnboarding.Features.DismissOnboardingChecklist;

internal sealed class DismissOnboardingChecklistHandler(
    CompanyOnboardingDbContext dbContext,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<DismissOnboardingChecklistResponse>> HandleAsync(
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<DismissOnboardingChecklistResponse>(Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        var progress = await dbContext.Progress
            .SingleOrDefaultAsync(p => p.CompanyId == companyId, cancellationToken);

        if (progress is null)
        {
            progress = CompanyOnboardingProgress.Create(companyId, now);
            dbContext.Progress.Add(progress);
        }

        progress.MarkDismissed(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new DismissOnboardingChecklistResponse(progress.IsHidden));
    }
}
