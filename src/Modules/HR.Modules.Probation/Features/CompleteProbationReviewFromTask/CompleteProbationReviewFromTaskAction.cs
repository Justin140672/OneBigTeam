using HR.Modules.Tasks.Contracts;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CompleteProbationReviewFromTask;

internal sealed class CompleteProbationReviewFromTaskAction(
    ProbationDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
    ProbationExtensionService extensionService,
    INotificationWriter notificationWriter) : ITaskCompletionAction
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

        if (review is null || review.Status != ProbationReviewStatus.Pending) return;

        var record = await dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.Id == review.ProbationRecordId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (record is null) return;

        var now          = clock.UtcNowOffset();
        var decisionDate = DateOnly.FromDateTime(now.DateTime);

        var (outcome, extensionEndDate) = ParseOutcome(context.OutcomeDecision);

        // PROB-05: the generic Tasks callback delivers the outcome as a raw string
        // (context.OutcomeDecision), so it needs the same rigor as the strongly-typed direct
        // CompleteProbationReview API path — reject malformed or absent outcomes cleanly before
        // any mutation. ParseOutcome already uses Enum-safe/TryParse-style parsing (see below) and
        // returns (null, null) for anything it doesn't recognise, so a null/mismatched outcome here
        // always means "malformed or absent" for this review's required outcome shape.
        if (review.ReviewType == ProbationReviewType.FinalDecision
            && outcome is not (ProbationOutcome.Pass or ProbationOutcome.Fail or ProbationOutcome.Extend))
            return;

        if (review.ReviewType == ProbationReviewType.ExtensionConfirmation
            && outcome != ProbationOutcome.Extend)
            return;

        if (review.ReviewType is not (ProbationReviewType.FinalDecision or ProbationReviewType.ExtensionConfirmation)
            && outcome.HasValue)
            return;

        // An Extend outcome without a parseable extension date is malformed — reject rather than
        // silently completing the review with no effective extension.
        if (outcome == ProbationOutcome.Extend && !extensionEndDate.HasValue)
            return;

        // PROB-05: extension end date must move strictly forward against both the record's
        // current expected end date and the decision date — same rule as the direct API path
        // (CompleteProbationReviewHandler). Reject without mutating anything if violated.
        if (outcome == ProbationOutcome.Extend
            && (extensionEndDate!.Value <= record.ExpectedEndDate || extensionEndDate.Value <= decisionDate))
            return;

        var previousExpectedEndDate = record.ExpectedEndDate;
        var extensionReason = context.OutcomeReason ?? "Probation extended.";

        if (outcome == ProbationOutcome.Pass)
            record.Pass(context.CompletedBy, decisionDate, context.OutcomeReason, now);
        else if (outcome == ProbationOutcome.Fail)
            record.Fail(context.CompletedBy, decisionDate, context.OutcomeReason, now);
        else if (outcome == ProbationOutcome.Extend && extensionEndDate.HasValue)
            record.Extend(extensionEndDate.Value, extensionReason, context.CompletedBy, decisionDate, now);

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

        if (outcome == ProbationOutcome.Extend && extensionEndDate.HasValue)
        {
            await extensionService.ApplyAsync(
                record,
                review,
                previousExpectedEndDate,
                extensionEndDate.Value,
                extensionReason,
                context.CompletedBy,
                decisionDate,
                now,
                cancellationToken);
        }

        if (outcome == ProbationOutcome.Pass)
        {
            await integrationEventPublisher.PublishAsync(
                new ProbationPassedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }

        // PROB-04: same employee-facing "outcome recorded" notification as the direct API path.
        // Extend is handled separately by extensionService.ApplyAsync above, which sends its own
        // notification.
        if (outcome is ProbationOutcome.Pass or ProbationOutcome.Fail)
        {
            await ProbationOutcomeNotifier.NotifyAsync(
                notificationWriter, record, review, now, cancellationToken);
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
