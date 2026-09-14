using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.PromoteEmployee;

internal sealed class PromoteEmployeeHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    CompensationRecordWriter compensationRecordWriter,
    IAuditEventPublisher auditEventPublisher,
    IEmployeePromotionFinalizer promotionFinalizer,
    IEmployeeTimelineWriter timelineWriter)
{
    public async Task<Result<PromoteEmployeeResponse>> HandleAsync(
        PromoteEmployeeRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, PromoteEmployeeResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<PromoteEmployeeResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<PromoteEmployeeResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = clock.TodayIn(timeZoneId);
        var isBackdated = request.EffectiveDate < today;

        if (isBackdated && !request.ConfirmBackdatedEffectiveDate)
            return Result.Failure<PromoteEmployeeResponse>(
                Error.Conflict(
                    "EffectiveDate is in the past. Confirm to backdate and apply the promotion immediately."));

        Guid? compensationId = null;

        if (request.CreateCompensationChange)
        {
            var compensationResult = await compensationRecordWriter.WriteAsync(
                request.CompanyId,
                request.EmployeeId,
                request.EffectiveDate,
                request.CompensationSalaryType!.Value,
                request.CompensationSalary!.Value,
                request.CompensationCurrency!,
                request.CompensationHoursPerWeek,
                request.CompensationFte,
                request.CompensationNotes,
                CompensationChangeReason.Promotion,
                actorEmployeeId,
                cancellationToken);

            if (compensationResult.IsFailure)
                return Result.Failure<PromoteEmployeeResponse>(compensationResult.Error);

            compensationId = compensationResult.Value!.Created.Id;
        }

        var previousPositionProfileId = employee.PositionProfileId;

        var now = clock.UtcNowOffset();

        var promotion = EmployeePromotion.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            previousPositionProfileId,
            request.NewPositionProfileId,
            request.NewManagerId,
            request.NewLocationId,
            request.EffectiveDate,
            request.Reason,
            request.Notes,
            compensationId,
            actorEmployeeId,
            now);

        dbContext.EmployeePromotions.Add(promotion);

        // Snapshot taken before finalization for use as the idempotency-replay payload only — a
        // replayed request never re-runs FinalizeAsync below, so its cached response must reflect
        // the state as of the original save, not the (possibly later-finalized) final state. The
        // actual method return value is rebuilt from final entity state further down instead.
        PromoteEmployeeResponse BuildResponse() => new(
            promotion.Id,
            promotion.CompanyId,
            promotion.EmployeeId,
            promotion.PreviousPositionProfileId,
            promotion.NewPositionProfileId,
            promotion.NewManagerId,
            promotion.NewLocationId,
            promotion.EffectiveDate,
            promotion.Reason,
            promotion.Notes,
            promotion.CompensationId,
            promotion.CreatedDate,
            promotion.CompletedAt);

        var response = BuildResponse();

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, PromoteEmployeeResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key - this attempt's
            // promotion row was rolled back along with it, so skip our own post-save side effects
            // and hand back the winner's result untouched.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new EmployeePromotionRequestedAuditEvent(
                promotion.CompanyId,
                promotion.EmployeeId,
                promotion.Id,
                actorEmployeeId,
                now,
                promotion.PreviousPositionProfileId,
                promotion.NewPositionProfileId,
                promotion.EffectiveDate,
                promotion.Reason),
            cancellationToken);

        // A same-day or backdated effective date should apply immediately rather than waiting for
        // tomorrow's job run — the handler and ProcessPromotionsJob's own catch-up condition both
        // use <= so coverage is seamless with no gap.
        if (request.EffectiveDate <= today)
        {
            await promotionFinalizer.FinalizeAsync(employee, promotion, actorEmployeeId, now, cancellationToken);
        }
        else
        {
            // Not finalized yet — FinalizeAsync (and the "Promoted" timeline entry it triggers via
            // EmployeePromotedIntegrationEvent) only runs once ProcessPromotionsJob reaches this
            // promotion's EffectiveDate. Write the entry eagerly here too, dated with EffectiveDate,
            // so a still-pending promotion is visible on the timeline (with the "Upcoming" badge)
            // rather than only appearing once it actually takes effect. sourceRecordId=promotion.Id
            // lets CreateTimelineEntryOnEmployeePromoted's own write (same EventType+SourceRecordId)
            // dedupe against this one when finalization eventually happens.
            var titles = await dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => p.CompanyId == request.CompanyId &&
                            (p.Id == previousPositionProfileId || p.Id == request.NewPositionProfileId))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

            var previousTitle = titles.GetValueOrDefault(previousPositionProfileId, "their previous role");
            var newTitle = titles.GetValueOrDefault(request.NewPositionProfileId, "a new role");

            await timelineWriter.TryAddAsync(
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(),
                    request.CompanyId,
                    request.EmployeeId,
                    request.EffectiveDate,
                    EmployeeTimelineEventType.EmployeePromoted,
                    EmployeeTimelineCategory.Employment,
                    "Promoted",
                    $"Promoted from {previousTitle} to {newTitle}.",
                    performedByUserId: null,
                    "Employees",
                    sourceRecordId: promotion.Id,
                    EmployeeTimelineVisibility.AuthorisedInternal,
                    now),
                cancellationToken);
        }

        return Result.Success(BuildResponse());
    }
}
