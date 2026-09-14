using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ApproveVacancy;

/// <summary>
/// SET-05: explicit approval step, required before PublishVacancyHandler will allow the vacancy to
/// be published when the company's VacancyApprovalRequired setting is on. Uses the "recruitment:manage"
/// policy — the same policy vacancy CRUD/publish already require — so a Recruiter can still approve
/// vacancies day-to-day; it is the separate hr-settings:manage-gated UpdateRecruitmentSettings
/// endpoint that a Recruiter cannot touch (see SET-05's authorisation requirement).
/// </summary>
internal sealed class ApproveVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<ApproveVacancyResponse>> HandleAsync(
        ApproveVacancyRequest request,
        Guid approvedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ApproveVacancyResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ApproveVacancyResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var vacancy = await db.Vacancies
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<ApproveVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        if (vacancy.Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            return Result.Failure<ApproveVacancyResponse>(
                Error.Validation($"Cannot approve a vacancy with status '{vacancy.Status}'."));

        var now = clock.UtcNowOffset();
        vacancy.Approve(approvedBy, now);

        var response = new ApproveVacancyResponse(
            vacancy.Id, vacancy.CompanyId, vacancy.ApprovedAt!.Value, vacancy.ApprovedByUserId!.Value);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, ApproveVacancyResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var effectiveTitle = vacancy.AdvertTitle
            ?? (await positionProfileReader.GetSummaryAsync(request.CompanyId, vacancy.PositionProfileId, cancellationToken))?.Title
            ?? "(untitled)";

        await auditPublisher.PublishAsync(
            new VacancyApprovedAuditEvent(vacancy.CompanyId, vacancy.Id, effectiveTitle, approvedBy, now),
            cancellationToken);

        return Result.Success(response);
    }
}
