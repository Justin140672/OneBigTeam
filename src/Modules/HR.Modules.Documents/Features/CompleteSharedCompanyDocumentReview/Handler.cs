using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;

internal sealed class CompleteSharedCompanyDocumentReviewHandler(
    DocumentsDbContext db,
    ITaskCompleter taskCompleter,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<CompleteSharedCompanyDocumentReviewResponse>> HandleAsync(
        CompleteSharedCompanyDocumentReviewRequest request,
        Guid reviewedBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the review completion.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CompleteSharedCompanyDocumentReviewResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CompleteSharedCompanyDocumentReviewResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<CompleteSharedCompanyDocumentReviewResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var reviewDate = DateOnly.FromDateTime(clock.UtcNow);
        var nextReviewDate = ComputeNextReviewDate(document.ReviewFrequency, document.CustomReviewFrequencyMonths, reviewDate);

        // Captured before CompleteReview overwrites document.ReviewDate — this is the due date
        // that THIS review is fulfilling, not the next scheduled one.
        var previousReviewDate = document.ReviewDate;

        document.CompleteReview(reviewedBy, request.ReviewNotes, reviewDate, nextReviewDate, clock.UtcNowOffset());

        var historyEntry = SharedCompanyDocumentReviewHistory.Create(
            Guid.NewGuid(),
            document.CompanyId,
            document.Id,
            reviewDate,
            reviewedBy,
            request.ReviewNotes,
            previousReviewDate,
            clock.UtcNowOffset());
        db.SharedCompanyDocumentReviewHistories.Add(historyEntry);

        var now = clock.UtcNowOffset();

        // Built from in-memory values ahead of the save (CompleteReview already ran above), so it
        // can double as both the response and the payload persisted for an idempotency replay.
        var response = new CompleteSharedCompanyDocumentReviewResponse(
            document.Id,
            document.CompanyId,
            document.ReviewDate,
            document.LastReviewedAt!.Value,
            document.LastReviewedByEmployeeId!.Value,
            document.LastReviewNotes);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key — this attempt's
            // changes were rolled back along with it, so skip the task-complete/audit publishing
            // below and hand back the winner's result untouched.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Closes the open Review task created by DetectDocumentsDueForReviewJob (sourceEntityId =
        // document.Id, TaskActionType.Review) so the next review cycle's ReviewDate isn't
        // permanently suppressed by a dangling open task — mirrors UploadRequestedDocumentHandler's
        // placement, after SaveChangesAsync so a task-completion failure can't roll back the review
        // itself. No-op if no matching open task exists.
        await taskCompleter.CompleteBySourceEntityAsync(
            document.CompanyId,
            document.Id,
            TaskSource.Document,
            TaskActionType.Review,
            reviewedBy,
            cancellationToken);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentReviewCompletedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            previousReviewDate,
            reviewDate,
            document.LastReviewNotes,
            nextReviewDate,
            reviewedBy,
            now), cancellationToken);

        return Result.Success(response);
    }

    // Deliberately kept out of the domain entity — CompleteReview receives the next review date
    // already computed, the same way UpdateDetails receives reviewDate/reviewFrequency as given
    // values rather than computing anything itself.
    private static DateOnly? ComputeNextReviewDate(
        SharedCompanyDocumentReviewFrequency frequency,
        int? customReviewFrequencyMonths,
        DateOnly reviewDate) =>
        frequency switch
        {
            SharedCompanyDocumentReviewFrequency.Monthly    => reviewDate.AddMonths(1),
            SharedCompanyDocumentReviewFrequency.Quarterly  => reviewDate.AddMonths(3),
            SharedCompanyDocumentReviewFrequency.SixMonthly => reviewDate.AddMonths(6),
            SharedCompanyDocumentReviewFrequency.Yearly     => reviewDate.AddMonths(12),
            // Defensive: Custom should always carry a value, but fall back to 0 months rather than
            // throwing if it somehow doesn't.
            SharedCompanyDocumentReviewFrequency.Custom     => reviewDate.AddMonths(customReviewFrequencyMonths ?? 0),
            _                                                => null,
        };
}
