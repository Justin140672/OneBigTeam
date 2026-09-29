using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

internal sealed class CompleteReturnToWorkReviewHandler(
    SicknessDbContext db,
    SicknessResourceAuthorizer authorizer,
    ITaskCompleter taskCompleter,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<CompleteReturnToWorkReviewResponse>> HandleAsync(
        CompleteReturnToWorkReviewRequest request,
        Guid reviewedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CompleteReturnToWorkReviewResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CompleteReturnToWorkReviewResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var review = await db.ReturnToWorkReviews
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ReviewId,
                cancellationToken);

        if (review is null)
            return Result.Failure<CompleteReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));

        var isHrAdministrator = await authorizer.IsHrAdministratorAsync(reviewedBy, cancellationToken);

        if (!isHrAdministrator)
        {
            var canView = await authorizer.CanViewEmployeeAsync(
                request.CompanyId, reviewedBy, review.EmployeeId, cancellationToken);

            if (!canView)
                return Result.Failure<CompleteReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));
        }

        var wasAlreadyCompleted = review.Status == ReturnToWorkReviewStatus.Completed;

        var now = clock.UtcNowOffset();

        if (!wasAlreadyCompleted)
        {
            review.Complete(
                reviewedBy,
                request.Outcome,
                request.AdjustmentsRequired,
                request.AdjustmentDetails,
                request.ManagerNotes,
                now);

            if (request.Outcome == FitToReturnOutcome.NotFit)
            {
                var sicknessRecord = await db.SicknessRecords
                    .FirstOrDefaultAsync(
                        s => s.Id == review.SicknessRecordId && s.CompanyId == request.CompanyId,
                        cancellationToken);

                if (sicknessRecord is not null)
                {
                    sicknessRecord.ReopenFollowingUnfitReview(now);

                    await auditPublisher.PublishAsync(new SicknessRecordReopenedAuditEvent(
                        sicknessRecord.CompanyId,
                        sicknessRecord.EmployeeId,
                        sicknessRecord.Id,
                        review.Id,
                        reviewedBy,
                        now), cancellationToken);
                }
            }

            var response = new CompleteReturnToWorkReviewResponse(
                review.Id,
                review.CompanyId,
                review.SicknessRecordId,
                review.EmployeeId,
                review.Status.ToString(),
                review.Outcome!.Value.ToString(),
                review.AdjustmentsRequired,
                review.AdjustmentDetails,
                review.ReviewedBy!.Value,
                review.CompletedAt!.Value);

            if (request.IdempotencyKey is { } key)
            {
                var outcome = await db.SaveIdempotentAsync(
                    db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

                if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                    return Result.Success(outcome.Response!);
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            await auditPublisher.PublishAsync(new ReturnToWorkReviewCompletedAuditEvent(
                review.Id,
                review.SicknessRecordId,
                review.CompanyId,
                review.EmployeeId,
                reviewedBy,
                review.Outcome!.Value.ToString(),
                review.AdjustmentsRequired,
                HasAdjustmentDetails: !string.IsNullOrWhiteSpace(review.AdjustmentDetails),
                HasNotes: !string.IsNullOrWhiteSpace(review.Notes),
                now,
                now), cancellationToken);

            await taskCompleter.CompleteBySourceEntityAsync(
                review.CompanyId,
                review.Id,
                TaskSource.Sickness,
                TaskActionType.Review,
                reviewedBy,
                cancellationToken);
        }

        return Result.Success(new CompleteReturnToWorkReviewResponse(
            review.Id,
            review.CompanyId,
            review.SicknessRecordId,
            review.EmployeeId,
            review.Status.ToString(),
            review.Outcome!.Value.ToString(),
            review.AdjustmentsRequired,
            review.AdjustmentDetails,
            review.ReviewedBy!.Value,
            review.CompletedAt!.Value));
    }
}
