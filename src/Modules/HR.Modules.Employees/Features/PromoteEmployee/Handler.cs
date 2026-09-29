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

        // Internal recruitment Ticket 7: the department moves with the position profile. Every
        // position profile belongs to a department, so capture it on the promotion now and let the
        // finalizer apply it on the effective date. When the profile can't be resolved the department
        // is left unchanged, which is the behaviour promotions had before this was added.
        var newDepartmentId = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId && p.Id == request.NewPositionProfileId)
            .Select(p => (Guid?)p.DepartmentId)
            .SingleOrDefaultAsync(cancellationToken);

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
            now,
            newDepartmentId: newDepartmentId);

        dbContext.EmployeePromotions.Add(promotion);

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

        if (request.EffectiveDate <= today)
        {
            await promotionFinalizer.FinalizeAsync(employee, promotion, actorEmployeeId, now, cancellationToken);
        }
        else
        {
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
