using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetMyEqualityData;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SaveMyEqualityData;

internal sealed class SaveMyEqualityDataHandler(
    EmployeesDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<GetMyEqualityDataResponse>> HandleAsync(
        SaveMyEqualityDataRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, GetMyEqualityDataResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<GetMyEqualityDataResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = clock.UtcNowOffset();

        var genderIdentity = EqualityEnumMapping.ToStored(request.GenderIdentity);
        var genderIdentitySelfDescribed = Trim(request.GenderIdentitySelfDescribed);
        var maritalStatus = EqualityEnumMapping.ToStored(request.MarriedOrCivilPartnershipStatus);
        var ethnicGroup = EqualityEnumMapping.ToStored(request.EthnicGroup);
        var ethnicGroupSelfDescribed = Trim(request.EthnicGroupSelfDescribed);
        var disabilityStatus = EqualityEnumMapping.ToStored(request.DisabilityStatus);
        var disabilityImpact = Trim(request.DisabilityImpact);
        var sexualOrientation = EqualityEnumMapping.ToStored(request.SexualOrientation);
        var sexualOrientationSelfDescribed = Trim(request.SexualOrientationSelfDescribed);
        var religionOrBelief = EqualityEnumMapping.ToStored(request.ReligionOrBelief);
        var religionOrBeliefSelfDescribed = Trim(request.ReligionOrBeliefSelfDescribed);
        var caringResponsibilities = EqualityEnumMapping.ToStored(request.CaringResponsibilities);

        var record = await db.EmployeeEqualityData
            .FirstOrDefaultAsync(
                x => x.CompanyId == request.CompanyId && x.EmployeeId == request.EmployeeId,
                cancellationToken);

        var created = record is null;

        if (record is null)
        {
            record = EmployeeEqualityData.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                genderIdentity,
                genderIdentitySelfDescribed,
                maritalStatus,
                ethnicGroup,
                ethnicGroupSelfDescribed,
                disabilityStatus,
                disabilityImpact,
                sexualOrientation,
                sexualOrientationSelfDescribed,
                religionOrBelief,
                religionOrBeliefSelfDescribed,
                caringResponsibilities,
                now);
            db.EmployeeEqualityData.Add(record);
        }
        else
        {
            record.Update(
                genderIdentity,
                genderIdentitySelfDescribed,
                maritalStatus,
                ethnicGroup,
                ethnicGroupSelfDescribed,
                disabilityStatus,
                disabilityImpact,
                sexualOrientation,
                sexualOrientationSelfDescribed,
                religionOrBelief,
                religionOrBeliefSelfDescribed,
                caringResponsibilities,
                now);
        }

        var response = EqualityDataResponseMapper.FromEntity(record);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, GetMyEqualityDataResponse>(db.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new EqualityDataUpdatedAuditEvent(
            record.CompanyId,
            record.EmployeeId,
            record.Id,
            created,
            genderIdentity is not null,
            maritalStatus is not null,
            ethnicGroup is not null,
            disabilityStatus is not null,
            sexualOrientation is not null,
            religionOrBelief is not null,
            caringResponsibilities is not null,
            now), cancellationToken);

        return Result.Success(response);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
