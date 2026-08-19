using HR.Modules.CompanyOnboarding.Domain;
using HR.Modules.CompanyOnboarding.Persistence;
using HR.Modules.CompanyOnboarding.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.CompanyOnboarding.Features.GetOnboardingChecklist;

internal sealed class GetOnboardingChecklistHandler(
    CompanyOnboardingDbContext dbContext,
    OnboardingTaskRegistry registry,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<GetOnboardingChecklistResponse>> HandleAsync(
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<GetOnboardingChecklistResponse>(Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        var progress = await dbContext.Progress
            .SingleOrDefaultAsync(p => p.CompanyId == companyId, cancellationToken);

        if (progress is null)
        {
            progress = CompanyOnboardingProgress.Create(companyId, now);
            dbContext.Progress.Add(progress);
        }

        var existingCompletions = await dbContext.TaskCompletions
            .Where(t => t.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var completionsByKey = existingCompletions.ToDictionary(t => t.TaskKey);

        var items = new List<OnboardingTaskItemResponse>();

        foreach (var task in registry.Tasks)
        {
            var isCompleted = await task.IsCompletedAsync(companyId, cancellationToken);
            var linkUrl = await task.GetLinkUrlAsync(companyId, cancellationToken);

            if (!completionsByKey.TryGetValue(task.Key, out var completion))
            {
                completion = CompanyOnboardingTaskCompletion.Create(Guid.NewGuid(), companyId, task.Key, now);
                dbContext.TaskCompletions.Add(completion);
                completionsByKey[task.Key] = completion;
            }

            completion.SetStatus(isCompleted, now);

            items.Add(new OnboardingTaskItemResponse(
                task.Key,
                task.Name,
                task.Description,
                task.IsMandatory,
                linkUrl,
                task.Order,
                isCompleted,
                completion.CompletedAt));
        }

        var mandatoryTotal = registry.Tasks.Count(t => t.IsMandatory);
        var mandatoryCompleted = items.Count(i => i.IsMandatory && i.IsCompleted);
        var completionPercentage = mandatoryTotal == 0
            ? 100
            : (int)Math.Round(100.0 * mandatoryCompleted / mandatoryTotal);

        if (completionPercentage == 100 && progress.CompletedAt is null)
        {
            progress.MarkCompleted(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new GetOnboardingChecklistResponse(
            items.OrderBy(i => i.Order).ToArray(),
            completionPercentage,
            progress.IsHidden,
            progress.IsDismissedEarly);

        return Result.Success(response);
    }
}
