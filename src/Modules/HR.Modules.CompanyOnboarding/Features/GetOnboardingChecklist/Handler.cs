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

        var completionsByKey = await EnsureCompletionRowsAsync(companyId, now, cancellationToken);

        var items = new List<OnboardingTaskItemResponse>();

        foreach (var task in registry.Tasks)
        {
            var liveComputedCompleted = await task.IsCompletedAsync(companyId, cancellationToken);
            var linkUrl = await task.GetLinkUrlAsync(companyId, cancellationToken);

            var completion = completionsByKey[task.Key];

            var isCompleted = liveComputedCompleted || completion.IsCompleted;

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

    private async Task<Dictionary<string, CompanyOnboardingTaskCompletion>> EnsureCompletionRowsAsync(
        Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var byKey = (await dbContext.TaskCompletions
                .Where(t => t.CompanyId == companyId)
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.TaskKey);

        var missing = registry.Tasks
            .Where(task => !byKey.ContainsKey(task.Key))
            .Select(task => CompanyOnboardingTaskCompletion.Create(Guid.NewGuid(), companyId, task.Key, now))
            .ToList();

        if (missing.Count == 0)
            return byKey;

        dbContext.TaskCompletions.AddRange(missing);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            foreach (var row in missing)
                dbContext.Entry(row).State = EntityState.Detached;

            return (await dbContext.TaskCompletions
                    .Where(t => t.CompanyId == companyId)
                    .ToListAsync(cancellationToken))
                .ToDictionary(t => t.TaskKey);
        }

        foreach (var row in missing)
            byKey[row.TaskKey] = row;

        return byKey;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } inner
        && inner.GetType().Name == "PostgresException"
        && string.Equals(
            inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string,
            "23505",
            StringComparison.Ordinal);
}
