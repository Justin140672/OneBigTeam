using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CreateProbationReview;

internal sealed class CreateProbationReviewHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;

    public CreateProbationReviewHandler(ProbationDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
    }

    public async Task<Result<CreateProbationReviewResponse>> HandleAsync(
        CreateProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, CreateProbationReviewResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateProbationReviewResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var record = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ProbationRecordId,
                cancellationToken);

        if (record is null)
            return Result.Failure<CreateProbationReviewResponse>(
                Error.NotFound("Probation record not found."));

        var reviewType = Enum.Parse<ProbationReviewType>(request.ReviewType, ignoreCase: true);

        var duplicateExists = await _dbContext.ProbationReviews
            .AnyAsync(
                r => r.CompanyId == request.CompanyId &&
                     r.ProbationRecordId == request.ProbationRecordId &&
                     r.ReviewType == reviewType,
                cancellationToken);

        if (duplicateExists)
            return Result.Failure<CreateProbationReviewResponse>(
                Error.Conflict($"A '{request.ReviewType}' review already exists for this probation record."));

        var review = ProbationReview.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.ProbationRecordId,
            reviewType,
            request.DueDate,
            _clock.UtcNowOffset());

        _dbContext.ProbationReviews.Add(review);

        var response = new CreateProbationReviewResponse(
            review.Id,
            review.CompanyId,
            review.ProbationRecordId,
            review.ReviewType.ToString(),
            review.DueDate,
            review.Status.ToString(),
            review.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync(_dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status201Created, response, review.CreatedAt, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditPublisher.PublishAsync(new ProbationReviewCreatedAuditEvent(
            review.CompanyId,
            review.Id,
            review.ProbationRecordId,
            record.EmployeeId,
            request.ActorEmployeeId,
            review.ReviewType.ToString(),
            review.DueDate,
            review.CreatedAt), cancellationToken);

        return Result.Success(response);
    }
}
