using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.MarkProbationNotApplicable;

/// <summary>
/// PROB-06: the explicit "probation does not apply" decision. Two cases:
///   1. An in-flight record (NotStarted or Active — see ProbationRecord.AllowedTransitions)
///      already exists for the employee: transition it to NotApplicable.
///   2. No record exists at all yet (creation was deferred for lack of a manager/period): create a
///      placeholder NotApplicable record directly, using the Manager/Start/ExpectedEnd fields the
///      caller supplied, so the decision is captured and auditable even though probation itself
///      never actually started.
/// A record already in a decided/terminal status (Passed/Failed/NotApplicable) or actively under
/// review (ReviewDue/Extended) is left alone and rejected with a Conflict — see
/// ProbationRecord.AllowedTransitions for why those statuses cannot become NotApplicable.
/// </summary>
internal sealed class MarkProbationNotApplicableHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;

    public MarkProbationNotApplicableHandler(
        ProbationDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
    }

    public async Task<Result<MarkProbationNotApplicableResponse>> HandleAsync(
        MarkProbationNotApplicableRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, MarkProbationNotApplicableResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<MarkProbationNotApplicableResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = _clock.UtcNowOffset();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        var existing = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.Status is ProbationStatus.NotStarted or ProbationStatus.Active)
            {
                existing.MarkNotApplicable(reason, now);

                var existingResponse = new MarkProbationNotApplicableResponse(
                    existing.Id, existing.CompanyId, existing.EmployeeId,
                    existing.Status.ToString(), existing.NotApplicableReason, existing.UpdatedAt);

                // Ticket 16 (optimistic concurrency) classification: a one-directional applicability
                // decision, not an independently-loaded edit-form lifecycle — the shared
                // VersionAdvancingSaveChangesInterceptor advances Version automatically here,
                // sufficient to make a concurrently-loaded UpdateProbationRecord screen stale.
                if (request.IdempotencyKey is { } existingKey)
                {
                    var existingOutcome = await _dbContext.SaveIdempotentAsync(_dbContext.IdempotencyRecords,
                        scope, existingKey, fingerprint!, StatusCodes.Status200OK, existingResponse, now, cancellationToken);

                    if (existingOutcome.Kind == IdempotencyOutcomeKind.Replayed)
                        return Result.Success(existingOutcome.Response!);
                }
                else
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await _auditPublisher.PublishAsync(new ProbationMarkedNotApplicableAuditEvent(
                    existing.CompanyId, existing.Id, existing.EmployeeId, request.ActorEmployeeId,
                    HasReason: reason is not null, now), cancellationToken);

                return Result.Success(existingResponse);
            }

            return Result.Failure<MarkProbationNotApplicableResponse>(
                Error.Conflict(
                    $"Cannot mark probation not applicable for a record in status '{existing.Status}'."));
        }

        if (request.ManagerEmployeeId is null || request.StartDate is null || request.ExpectedEndDate is null)
        {
            return Result.Failure<MarkProbationNotApplicableResponse>(
                Error.Validation(
                    "ManagerEmployeeId, StartDate and ExpectedEndDate are required to mark probation not " +
                    "applicable for an employee with no existing probation record."));
        }

        var record = ProbationRecord.CreateNotApplicable(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.ManagerEmployeeId.Value,
            request.StartDate.Value,
            request.ExpectedEndDate.Value,
            reason,
            now);

        _dbContext.ProbationRecords.Add(record);

        var response = new MarkProbationNotApplicableResponse(
            record.Id, record.CompanyId, record.EmployeeId,
            record.Status.ToString(), record.NotApplicableReason, record.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync(_dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditPublisher.PublishAsync(new ProbationMarkedNotApplicableAuditEvent(
            record.CompanyId, record.Id, record.EmployeeId, request.ActorEmployeeId,
            HasReason: reason is not null, now), cancellationToken);

        return Result.Success(response);
    }
}
