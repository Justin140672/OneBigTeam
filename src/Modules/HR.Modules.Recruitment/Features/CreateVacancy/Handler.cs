using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class CreateVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    RecruitmentStageSeeder stageSeeder)
{
    public async Task<Result<CreateVacancyResponse>> HandleAsync(
        CreateVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateVacancyResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateVacancyResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var positionProfileExists = await positionProfileReader.ExistsAsync(
            request.CompanyId, request.PositionProfileId, cancellationToken);

        if (!positionProfileExists)
            return Result.Failure<CreateVacancyResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var hasConcurrentVacancy = await db.Vacancies
            .AsNoTracking()
            .AnyAsync(
                v => v.CompanyId == request.CompanyId
                    && v.PositionProfileId == request.PositionProfileId
                    && v.Status != VacancyStatus.Closed
                    && v.Status != VacancyStatus.Cancelled,
                cancellationToken);

        if (hasConcurrentVacancy)
            return Result.Failure<CreateVacancyResponse>(
                Error.Validation("This position profile already has an open vacancy. Close or cancel it before opening another."));

        if (request.AssignedRecruiterId is { } requestedRecruiterId)
        {
            var recruiter = await db.ExternalRecruiters
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.Id == requestedRecruiterId && r.CompanyId == request.CompanyId,
                    cancellationToken);

            if (recruiter is null)
                return Result.Failure<CreateVacancyResponse>(
                    Error.NotFound($"External recruiter '{requestedRecruiterId}' was not found."));

            if (!recruiter.IsActive)
                return Result.Failure<CreateVacancyResponse>(
                    Error.Validation($"External recruiter '{recruiter.AgencyName}' is inactive and cannot be assigned to a vacancy."));
        }

        var now = clock.UtcNowOffset();

        await stageSeeder.EnsureDefaultStagesSeededAsync(request.CompanyId, now, cancellationToken);

        var vacancy = Vacancy.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.AdvertTitle,
            request.AdvertDescription,
            request.HiringManagerId,
            now,
            request.AssignedRecruiterId,
            request.IsAdvertisedInternally);

        db.Vacancies.Add(vacancy);

        var response = new CreateVacancyResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.PositionProfileId,
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.Status,
            vacancy.HiringManagerId,
            vacancy.AssignedRecruiterId,
            vacancy.IsAdvertisedInternally,
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, CreateVacancyResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
