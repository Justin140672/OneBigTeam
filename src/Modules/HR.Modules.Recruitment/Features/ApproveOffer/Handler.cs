using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ApproveOffer;

/// <summary>
/// SET-05: explicit per-application approval step, required before OfferCandidateHandler will allow
/// the application to move to the offer stage when the company's OfferApprovalRequired setting is
/// on. Uses "recruitment:manage", the same policy the offer action itself requires.
/// </summary>
internal sealed class ApproveOfferHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ApproveOfferResponse>> HandleAsync(
        ApproveOfferRequest request,
        Guid approvedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ApproveOfferResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ApproveOfferResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<ApproveOfferResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<ApproveOfferResponse>(
                Error.Validation("Cannot approve an offer for an application that has been withdrawn."));

        var now = clock.UtcNowOffset();
        var expectedVersion = application.Version;
        application.ApproveOffer(approvedBy, now);

        var response = new ApproveOfferResponse(
            application.Id, application.CompanyId, application.OfferApprovedAt!.Value, application.OfferApprovedByUserId!.Value);

        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Domain.Application, ApproveOfferResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<ApproveOfferResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<ApproveOfferResponse>(saveResult.Error);
        }

        await auditPublisher.PublishAsync(
            new OfferApprovedAuditEvent(
                application.CompanyId, application.Id, application.VacancyId, application.CandidateId, approvedBy, now),
            cancellationToken);

        return Result.Success(response);
    }
}
