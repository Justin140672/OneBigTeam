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

            // Sticky: a manual completion (e.g. via MarkOnboardingTaskComplete, used for
            // "Download the Employee import template") is never reverted by a later checklist
            // load just because the task's own live computation currently evaluates to false.
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

    /// <summary>
    /// Lazily materialises a <see cref="CompanyOnboardingTaskCompletion"/> row for every registry
    /// task, returning the full set keyed by task key. This GET is hit concurrently (dashboard +
    /// getting-started widget, multiple tabs/circuits), so a plain read-then-insert races: two
    /// callers both see a missing row and both INSERT, the second violating
    /// <c>IX_task_completions_company_id_task_key</c> and 500-ing the request. Insert the missing
    /// rows in their own SaveChanges; if a concurrent caller won the race (23505), swallow it,
    /// re-read, and carry on — the row now exists either way. Status updates happen on the caller's
    /// own SaveChanges afterwards.
    /// </summary>
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
            // A concurrent request inserted (some of) these first. Detach our losing inserts and
            // re-read the now-complete set.
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
