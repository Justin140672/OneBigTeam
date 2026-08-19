using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CloseVacancy;

internal sealed class CloseVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<CloseVacancyResponse>> HandleAsync(
        CloseVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<CloseVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        if (vacancy.Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            return Result.Failure<CloseVacancyResponse>(
                Error.Validation($"Cannot close a vacancy with status '{vacancy.Status}'."));

        var previousStatus = vacancy.Status;
        var now = clock.UtcNowOffset();
        var closedAt = request.ClosedAt ?? DateOnly.FromDateTime(now.UtcDateTime);

        vacancy.Close(now, closedAt);
        await db.SaveChangesAsync(cancellationToken);

        // Cross-module read purely for a readable audit Summary line — see VacancyClosedAuditEvent's
        // remarks and the identical pattern in UpdateVacancyHandler.
        var effectiveTitle = vacancy.AdvertTitle
            ?? (await positionProfileReader.GetSummaryAsync(request.CompanyId, vacancy.PositionProfileId, cancellationToken))?.Title
            ?? "(untitled)";

        await auditPublisher.PublishAsync(
            new VacancyClosedAuditEvent(vacancy.CompanyId, vacancy.Id, effectiveTitle, previousStatus, closedAt, now),
            cancellationToken);

        return Result.Success(new CloseVacancyResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.PositionProfileId,
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.Status,
            vacancy.HiringManagerId,
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt));
    }
}
