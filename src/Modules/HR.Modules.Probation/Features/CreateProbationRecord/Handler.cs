using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed class CreateProbationRecordHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly ICompanyTimeZoneReader _timeZoneReader;

    public CreateProbationRecordHandler(
        ProbationDbContext dbContext,
        IClock clock,
        IAuditEventPublisher auditPublisher,
        ICompanyTimeZoneReader timeZoneReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
        _timeZoneReader = timeZoneReader;
    }

    public async Task<Result<CreateProbationRecordResponse>> HandleAsync(
        CreateProbationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, CreateProbationRecordResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateProbationRecordResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var hasActive = await _dbContext.ProbationRecords
            .AnyAsync(
                r => r.CompanyId == request.CompanyId &&
                     r.EmployeeId == request.EmployeeId &&
                     (r.Status == ProbationStatus.Active ||
                      r.Status == ProbationStatus.ReviewDue ||
                      r.Status == ProbationStatus.Extended),
                cancellationToken);

        if (hasActive)
        {
            return Result.Failure<CreateProbationRecordResponse>(
                Error.Conflict("An active probation record already exists for this employee."));
        }

        var now = _clock.UtcNowOffset();
        var timeZoneId = await _timeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = _clock.TodayIn(timeZoneId);

        var record = ProbationRecord.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.ManagerEmployeeId,
            request.StartDate,
            request.ExpectedEndDate,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            today,
            now);

        _dbContext.ProbationRecords.Add(record);

        var response = new CreateProbationRecordResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.ManagerEmployeeId,
            record.StartDate,
            record.ExpectedEndDate,
            record.Status.ToString(),
            record.Notes,
            record.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync(_dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditPublisher.PublishAsync(new ProbationRecordCreatedAuditEvent(
            record.CompanyId,
            record.Id,
            record.EmployeeId,
            record.ManagerEmployeeId,
            request.ActorEmployeeId,
            record.StartDate,
            record.ExpectedEndDate,
            HasNotes: !string.IsNullOrWhiteSpace(record.Notes),
            now), cancellationToken);

        return Result.Success(response);
    }
}
