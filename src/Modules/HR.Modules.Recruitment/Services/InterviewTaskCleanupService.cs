using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InterviewTaskCleanupService(
    RecruitmentDbContext db,
    ITaskCanceller taskCanceller,
    IClock clock,
    ILogger<InterviewTaskCleanupService> logger)
{
    private const int BatchSize = 100;

    public async Task<int> RunOutstandingForApplicationAsync(
        Guid companyId, Guid applicationId, CancellationToken cancellationToken)
    {
        var outstanding = await db.InterviewTaskCleanups
            .Where(c => c.CompanyId == companyId && c.ApplicationId == applicationId && c.CompletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var cleanup in outstanding)
            await RunAsync(cleanup, cancellationToken);

        return outstanding.Count;
    }

    public async Task<int> RunAllOutstandingAsync(CancellationToken cancellationToken)
    {
        var outstanding = await db.InterviewTaskCleanups
            .Where(c => c.CompletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var cleanup in outstanding)
            await RunAsync(cleanup, cancellationToken);

        return outstanding.Count;
    }

    public async Task<bool> RunAsync(InterviewTaskCleanup cleanup, CancellationToken cancellationToken)
    {
        try
        {
            var interviewIds = cleanup.InterviewIds;

            if (interviewIds.Count > 0)
            {
                await taskCanceller.CancelManyBySourceEntitiesAsync(
                    cleanup.CompanyId, interviewIds, TaskSource.Recruitment, TaskActionType.Review, cancellationToken);
                await taskCanceller.CancelManyBySourceEntitiesAsync(
                    cleanup.CompanyId, interviewIds, TaskSource.Recruitment, TaskActionType.Complete, cancellationToken);
            }

            cleanup.MarkCompleted(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Interview task cleanup {CleanupId} for application {ApplicationId} failed and will be retried.",
                cleanup.Id, cleanup.ApplicationId);

            try
            {
                cleanup.MarkFailed(ex.Message, clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to record interview task cleanup failure {CleanupId}.", cleanup.Id);
            }

            return false;
        }
    }
}
