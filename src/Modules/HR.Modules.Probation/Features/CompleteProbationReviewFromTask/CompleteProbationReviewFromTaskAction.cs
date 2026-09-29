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

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated probation review."));

        var review = await dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (review is null)
            return Result.Failure(Error.NotFound("The associated probation review was not found."));

        var record = await dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.Id == review.ProbationRecordId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (record is null)
            return Result.Failure(Error.NotFound("The associated probation record was not found."));

        // Ticket 15 (P1): "already resolved" (e.g. via the direct CompleteProbationReview API
        // path, or a retried/replayed Tasks-dispatch) proves only that the review's own status
        // transition committed — it says nothing about whether the follow-up effects (audit,
        // extension review/task pair, integration event, notification) that a prior attempt may
        // have been interrupted before reaching were ever applied. review.Outcome was persisted
        // atomically with that same transition, so it can be used to safely reconstruct and
        // recover exactly what a completed-but-interrupted dispatch still owes.
        if (review.Status != ProbationReviewStatus.Pending)
        {
            await RecoverReviewEffectsAsync(review, record, context, cancellationToken);
            return Result.Success();
        }

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
            return Result.Failure(Error.Validation(
                "A decision of Pass, Fail, or Extend is required to complete this review."));

        if (review.ReviewType == ProbationReviewType.ExtensionConfirmation
            && outcome != ProbationOutcome.Extend)
            return Result.Failure(Error.Validation(
                "An Extend decision with a valid extension date is required to complete this review."));

        if (review.ReviewType is not (ProbationReviewType.FinalDecision or ProbationReviewType.ExtensionConfirmation)
            && outcome.HasValue)
            return Result.Failure(Error.Validation(
                "This review type does not accept a Pass/Fail/Extend decision."));

        // An Extend outcome without a parseable extension date is malformed — reject rather than
        // silently completing the review with no effective extension.
        if (outcome == ProbationOutcome.Extend && !extensionEndDate.HasValue)
            return Result.Failure(Error.Validation(
                "A valid extension end date is required to extend probation."));

        // PROB-05: extension end date must move strictly forward against both the record's
        // current expected end date and the decision date — same rule as the direct API path
        // (CompleteProbationReviewHandler). Reject without mutating anything if violated.
        if (outcome == ProbationOutcome.Extend
            && (extensionEndDate!.Value <= record.ExpectedEndDate || extensionEndDate.Value <= decisionDate))
            return Result.Failure(Error.Validation(
                "The extension end date must be after both the current expected end date and today."));

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

        var hasNotes = !string.IsNullOrWhiteSpace(review.Notes);

        if (outcome == ProbationOutcome.Pass)
        {
            await auditPublisher.PublishAsync(new ProbationPassedAuditEvent(
                review.CompanyId, review.ProbationRecordId, review.Id, record.EmployeeId,
                context.CompletedBy, decisionDate, hasNotes, now), cancellationToken);
        }
        else if (outcome == ProbationOutcome.Fail)
        {
            await auditPublisher.PublishAsync(new ProbationFailedAuditEvent(
                review.CompanyId, review.ProbationRecordId, review.Id, record.EmployeeId,
                context.CompletedBy, decisionDate, hasNotes, now), cancellationToken);
        }
        else if (outcome is null)
        {
            await auditPublisher.PublishAsync(new ProbationReviewCompletedAuditEvent(
                review.CompanyId, review.Id, review.ProbationRecordId, record.EmployeeId,
                context.CompletedBy, review.ReviewType.ToString(), hasNotes, now), cancellationToken);
        }

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
                cancellationToken,
                dispatchOperationId: context.DispatchOperationId == Guid.Empty ? null : context.DispatchOperationId);
        }

        if (outcome == ProbationOutcome.Pass)
        {
            await integrationEventPublisher.PublishAsync(
                new ProbationPassedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }
        else if (outcome == ProbationOutcome.Fail)
        {
            await integrationEventPublisher.PublishAsync(
                new ProbationFailedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }

        if (outcome is ProbationOutcome.Pass or ProbationOutcome.Fail)
        {
            await ProbationOutcomeNotifier.NotifyAsync(
                notificationWriter, record, review, now, cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Ticket 15 (P1): reconstructs which post-commit follow-ups a completed-but-possibly-
    /// interrupted review still owes, purely from the review's own persisted
    /// Outcome/CompletedByEmployeeId/Notes/CompletedAt — never from the (potentially stale, this
    /// call's own) TaskCompletionContext. Every effect below is itself idempotent under replay:
    /// the Pass/Fail/ReviewCompleted audit events carry a deterministic EventId (see
    /// ProbationAudit.cs), extensionService.ApplyAsync is replay-safe when given the same dispatch
    /// operation id, integration-event consumers are required to be idempotent, and
    /// ProbationOutcomeNotifier's notification is naturally deduped by NotificationWriter.
    /// </summary>
    private async Task RecoverReviewEffectsAsync(
        ProbationReview review,
        ProbationRecord record,
        TaskCompletionContext context,
        CancellationToken cancellationToken)
    {
        if (review.Status != ProbationReviewStatus.Completed)
            return;

        var now = clock.UtcNowOffset();
        var decisionDate = review.CompletedAt.HasValue
            ? DateOnly.FromDateTime(review.CompletedAt.Value.DateTime)
            : DateOnly.FromDateTime(now.DateTime);
        var outcome = review.Outcome;
        var completedBy = review.CompletedByEmployeeId ?? context.CompletedBy;
        var hasNotes = !string.IsNullOrWhiteSpace(review.Notes);

        if (outcome == ProbationOutcome.Pass)
        {
            await auditPublisher.PublishAsync(new ProbationPassedAuditEvent(
                review.CompanyId, review.ProbationRecordId, review.Id, record.EmployeeId,
                completedBy, decisionDate, hasNotes, now), cancellationToken);
        }
        else if (outcome == ProbationOutcome.Fail)
        {
            await auditPublisher.PublishAsync(new ProbationFailedAuditEvent(
                review.CompanyId, review.ProbationRecordId, review.Id, record.EmployeeId,
                completedBy, decisionDate, hasNotes, now), cancellationToken);
        }
        else if (outcome is null)
        {
            await auditPublisher.PublishAsync(new ProbationReviewCompletedAuditEvent(
                review.CompanyId, review.Id, review.ProbationRecordId, record.EmployeeId,
                completedBy, review.ReviewType.ToString(), hasNotes, now), cancellationToken);
        }

        // The Extend follow-up (new review pair + tasks) is only safely recoverable when this
        // dispatch carries a stable operation id — that id is what lets ProbationExtensionService
        // detect "I already created this pair" instead of inserting a second one. Without it (e.g.
        // a bare retry of the same request with no durable dispatch identity), there is no reliable
        // way to distinguish "recover the missed pair" from "create a brand new duplicate pair", so
        // this deliberately stays a no-op here — exactly the same safety margin the pre-Ticket-15
        // short-circuit gave every caller in that situation.
        if (outcome == ProbationOutcome.Extend && context.DispatchOperationId != Guid.Empty)
        {
            await extensionService.ApplyAsync(
                record,
                review,
                previousExpectedEndDate: record.ExpectedEndDate,
                newExpectedEndDate: record.ExpectedEndDate,
                extensionReason: context.OutcomeReason ?? "Probation extended.",
                completedBy,
                decisionDate,
                now,
                cancellationToken,
                dispatchOperationId: context.DispatchOperationId);
        }

        if (outcome == ProbationOutcome.Pass)
        {
            await integrationEventPublisher.PublishAsync(
                new ProbationPassedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }
        else if (outcome == ProbationOutcome.Fail)
        {
            await integrationEventPublisher.PublishAsync(
                new ProbationFailedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }

        if (outcome is ProbationOutcome.Pass or ProbationOutcome.Fail)
        {
            await ProbationOutcomeNotifier.NotifyAsync(
                notificationWriter, record, review, now, cancellationToken);
        }
    }

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
