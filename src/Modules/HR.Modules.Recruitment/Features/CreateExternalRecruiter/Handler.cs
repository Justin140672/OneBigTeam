using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateExternalRecruiter;

internal sealed class CreateExternalRecruiterHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateExternalRecruiterResponse>> HandleAsync(
        CreateExternalRecruiterRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateExternalRecruiterResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateExternalRecruiterResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        // Duplicate agency names are explicitly allowed — no uniqueness validation performed here.
        var now = clock.UtcNowOffset();

        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.AgencyName,
            request.ContactName,
            request.ContactEmail,
            request.ContactTelephone,
            request.Website,
            request.Notes,
            now);

        db.ExternalRecruiters.Add(recruiter);

        var response = new CreateExternalRecruiterResponse(
            recruiter.Id,
            recruiter.CompanyId,
            recruiter.AgencyName,
            recruiter.ContactName,
            recruiter.ContactEmail,
            recruiter.ContactTelephone,
            recruiter.Website,
            recruiter.Notes,
            recruiter.IsActive,
            recruiter.CreatedAt,
            recruiter.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, CreateExternalRecruiterResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new ExternalRecruiterCreatedAuditEvent(recruiter.CompanyId, recruiter.Id, recruiter.AgencyName, now),
            cancellationToken);

        return Result.Success(response);
    }
}
