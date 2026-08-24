using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed class CompleteProbationReviewHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly ProbationExtensionService _extensionService;
    private readonly INotificationWriter _notificationWriter;

    public CompleteProbationReviewHandler(
        ProbationDbContext dbContext,
        IClock clock,
        IAuditEventPublisher auditPublisher,
        IIntegrationEventPublisher integrationEventPublisher,
        ProbationExtensionService extensionService,
        INotificationWriter notificationWriter)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
        _integrationEventPublisher = integrationEventPublisher;
        _extensionService = extensionService;
        _notificationWriter = notificationWriter;
    }

    public async Task<Result<CompleteProbationReviewResponse>> HandleAsync(
        CompleteProbationReviewRequest request,
        Guid completedByEmployeeId,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ProbationRecordId,
                cancellationToken);

        if (record is null)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.NotFound("Probation record not found."));

        var review = await _dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId
                     && r.ProbationRecordId == request.ProbationRecordId
                     && r.Id == request.ReviewId,
                cancellationToken);

        if (review is null)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.NotFound("Probation review not found."));

        if (review.Status == ProbationReviewStatus.Completed)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("Probation review is already completed."));

        if (review.Status == ProbationReviewStatus.Cancelled)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("Probation review has been superseded and can no longer be completed."));

        if (review.ReviewType == ProbationReviewType.FinalDecision
            && request.Outcome is not (ProbationOutcome.Pass or ProbationOutcome.Fail or ProbationOutcome.Extend))
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("A Pass, Fail, or Extend outcome is required when completing a FinalDecision review."));

        if (review.ReviewType == ProbationReviewType.ExtensionConfirmation
            && request.Outcome != ProbationOutcome.Extend)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("An Extend outcome is required when completing an ExtensionConfirmation review."));

        if (review.ReviewType is not (ProbationReviewType.FinalDecision or ProbationReviewType.ExtensionConfirmation)
            && request.Outcome.HasValue)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("Outcome can only be set on FinalDecision or ExtensionConfirmation reviews."));

        // PROB-05: extension end date must move strictly forward — both relative to the record's
        // current expected end date and relative to the decision date itself. Checked here (in
        // addition to the domain-level guard inside ProbationRecord.Extend) so a caller gets a
        // clean validation Result instead of an unhandled exception, and so no review/task/record
        // mutation happens when the check fails.
        if (request.Outcome == ProbationOutcome.Extend)
        {
            if (request.NewExpectedEndDate!.Value <= record.ExpectedEndDate)
                return Result.Failure<CompleteProbationReviewResponse>(
                    Error.Validation("NewExpectedEndDate must be later than the current expected end date."));

            if (request.NewExpectedEndDate!.Value <= request.DecisionDate!.Value)
                return Result.Failure<CompleteProbationReviewResponse>(
                    Error.Validation("NewExpectedEndDate must be later than the decision date."));
        }

        var now = _clock.UtcNowOffset();
        var previousExpectedEndDate = record.ExpectedEndDate;

        if (request.Outcome == ProbationOutcome.Pass)
            record.Pass(completedByEmployeeId, request.DecisionDate!.Value, request.Notes, now);
        else if (request.Outcome == ProbationOutcome.Fail)
            record.Fail(completedByEmployeeId, request.DecisionDate!.Value, request.Notes, now);
        else if (request.Outcome == ProbationOutcome.Extend)
            record.Extend(request.NewExpectedEndDate!.Value, request.ExtensionReason!, completedByEmployeeId, request.DecisionDate!.Value, now);

        review.Complete(completedByEmployeeId, request.Outcome, request.Notes, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (request.Outcome == ProbationOutcome.Extend)
        {
            await _extensionService.ApplyAsync(
                record,
                review,
                previousExpectedEndDate,
                request.NewExpectedEndDate!.Value,
                request.ExtensionReason!,
                completedByEmployeeId,
                request.DecisionDate!.Value,
                now,
                cancellationToken);
        }

        await _auditPublisher.PublishAsync(new ProbationReviewCompletedAuditEvent(
            review.CompanyId,
            review.Id,
            review.ProbationRecordId,
            record.EmployeeId,
            completedByEmployeeId,
            review.ReviewType.ToString(),
            review.Outcome?.ToString(),
            review.Notes,
            now), cancellationToken);

        // Only a Pass outcome maps to the timeline's ProbationPassed event this wave — Fail and
        // Extend outcomes do not get a dedicated timeline entry (see Wave 2a scope notes).
        if (request.Outcome == ProbationOutcome.Pass)
        {
            await _integrationEventPublisher.PublishAsync(
                new ProbationPassedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, now),
                cancellationToken);
        }

        // PROB-04: notify the employee when a Pass/Fail outcome is recorded. Extend is handled
        // separately by ProbationExtensionService.ApplyAsync (called above), which already sends
        // its own "probation extended" notification — sending another one here would duplicate it.
        if (request.Outcome is ProbationOutcome.Pass or ProbationOutcome.Fail)
        {
            await ProbationOutcomeNotifier.NotifyAsync(
                _notificationWriter, record, review, now, cancellationToken);
        }

        return Result.Success(new CompleteProbationReviewResponse(
            review.Id,
            review.CompanyId,
            review.ProbationRecordId,
            review.ReviewType.ToString(),
            review.DueDate,
            review.Status.ToString(),
            review.CompletedAt,
            review.CompletedByEmployeeId,
            review.Outcome?.ToString(),
            review.Notes));
    }
}
