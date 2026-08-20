using HR.Modules.CompanyOnboarding.Domain;
using HR.Modules.CompanyOnboarding.Persistence;
using HR.Modules.CompanyOnboarding.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.CompanyOnboarding.Features.MarkOnboardingTaskComplete;

/// <summary>
/// Marks a single onboarding checklist task as manually completed, independent of the task's own
/// live IsCompletedAsync computation. Used for tasks like "Download the Employee import template"
/// (see DownloadEmployeeImportTemplateTask) where the meaningful user action (clicking Download)
/// has no other durable signal to key off — unlike ImportEmployeesTask, which is satisfied purely
/// by data already present (an actual employee import). Once set, GetOnboardingChecklistHandler
/// treats the persisted completion as sticky (OR'd with the live computed value) so a manual
/// completion is never silently reverted on a later checklist load.
/// </summary>
internal sealed class MarkOnboardingTaskCompleteHandler(
    CompanyOnboardingDbContext dbContext,
    OnboardingTaskRegistry registry,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<MarkOnboardingTaskCompleteResponse>> HandleAsync(
        MarkOnboardingTaskCompleteRequest request,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<MarkOnboardingTaskCompleteResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        if (registry.Tasks.All(t => t.Key != request.TaskKey))
        {
            return Result.Failure<MarkOnboardingTaskCompleteResponse>(
                Error.NotFound($"Onboarding task '{request.TaskKey}' was not found."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        var completion = await dbContext.TaskCompletions
            .SingleOrDefaultAsync(t => t.CompanyId == companyId && t.TaskKey == request.TaskKey, cancellationToken);

        if (completion is null)
        {
            completion = CompanyOnboardingTaskCompletion.Create(Guid.NewGuid(), companyId, request.TaskKey, now);
            dbContext.TaskCompletions.Add(completion);
        }

        completion.SetStatus(true, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new MarkOnboardingTaskCompleteResponse(request.TaskKey, completion.IsCompleted, completion.CompletedAt));
    }
}
