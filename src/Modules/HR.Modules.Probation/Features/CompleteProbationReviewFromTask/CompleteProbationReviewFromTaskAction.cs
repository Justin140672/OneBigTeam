using HR.Modules.Tasks.Contracts;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CompleteProbationReviewFromTask;

internal sealed class CompleteProbationReviewFromTaskAction(
    ProbationDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Probation;
    public TaskActionType ActionType => TaskActionType.Review;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null) return;

        var review = await dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (review is null || review.Status == ProbationReviewStatus.Completed) return;

        var record = await dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.Id == review.ProbationRecordId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (record is null) return;

        var now          = clock.UtcNowOffset();
        var decisionDate = DateOnly.FromDateTime(now.DateTime);

        var (outcome, extensionEndDate) = ParseOutcome(context.OutcomeDecision);

        if (outcome == ProbationOutcome.Pass)
            record.Pass(context.CompletedBy, decisionDate, context.OutcomeReason, now);
        else if (outcome == ProbationOutcome.Fail)
            record.Fail(context.CompletedBy, decisionDate, context.OutcomeReason, now);
        else if (outcome == ProbationOutcome.Extend && extensionEndDate.HasValue)
            record.Extend(extensionEndDate.Value, context.OutcomeReason ?? "Probation extended.", context.CompletedBy, decisionDate, now);

        review.Complete(context.CompletedBy, outcome, context.OutcomeReason, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new ProbationReviewCompletedAuditEvent(
            review.CompanyId,
            review.Id,
            review.ProbationRecordId,
            record.EmployeeId,
            context.CompletedBy,
            review.ReviewType.ToString(),
            review.Outcome?.ToString(),
            review.Notes,
            now), cancellationToken);

        if (outcome == ProbationOutcome.Pass)
        {
            await integrationEventPublisher.PublishAsync(
                new ProbationPassedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }
    }

    // OutcomeDecision is "Pass", "Fail", or "Extend|yyyy-MM-dd".
    private static (ProbationOutcome? outcome, DateOnly? extensionEndDate) ParseOutcome(string? outcomeDecision)
    {
        if (outcomeDecision is null) return (null, null);

        if (outcomeDecision.StartsWith("Extend|", StringComparison.Ordinal)
            && DateOnly.TryParse(outcomeDecision[7..], out var d))
            return (ProbationOutcome.Extend, d);

        return outcomeDecision switch
        {
            "Pass" => (ProbationOutcome.Pass, null),
            "Fail" => (ProbationOutcome.Fail, null),
            _      => (null, null)
        };
    }
}
