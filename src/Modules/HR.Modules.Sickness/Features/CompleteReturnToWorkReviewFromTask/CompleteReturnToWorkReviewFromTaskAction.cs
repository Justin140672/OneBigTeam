using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask;

internal sealed class CompleteReturnToWorkReviewFromTaskAction(
    SicknessDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditPublisher) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Sickness;
    public TaskActionType ActionType => TaskActionType.Review;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        var review = await dbContext.ReturnToWorkReviews
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId.Value && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (review is null || review.Status == ReturnToWorkReviewStatus.Completed)
            return;

        var now = clock.UtcNowOffset();

        review.Complete(context.CompletedBy, context.OutcomeReason, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new ReturnToWorkReviewCompletedAuditEvent(
                ReviewId:         review.Id,
                SicknessRecordId: review.SicknessRecordId,
                CompanyId:        review.CompanyId,
                EmployeeId:       review.EmployeeId,
                ReviewedBy:       context.CompletedBy,
                Notes:            review.Notes,
                CompletedAt:      now,
                OccurredAt:       now),
            cancellationToken);
    }
}
