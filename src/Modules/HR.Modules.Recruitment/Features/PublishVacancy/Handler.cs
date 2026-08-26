using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.PublishVacancy;

internal sealed class PublishVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IPositionProfileReader positionProfileReader,
    ICompanyRecruitmentSettingsReader recruitmentSettingsReader)
{
    public async Task<Result<PublishVacancyResponse>> HandleAsync(
        PublishVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<PublishVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        if (vacancy.Status is not (VacancyStatus.Draft or VacancyStatus.OnHold))
            return Result.Failure<PublishVacancyResponse>(
                Error.Validation($"Cannot publish a vacancy with status '{vacancy.Status}'."));

        // SET-05: when the company requires vacancy approval, a vacancy cannot be published until
        // it has been explicitly approved via the ApproveVacancy endpoint.
        var recruitmentSettings = await recruitmentSettingsReader.GetRecruitmentSettingsAsync(request.CompanyId, cancellationToken);
        if (recruitmentSettings.VacancyApprovalRequired && vacancy.ApprovedAt is null)
            return Result.Failure<PublishVacancyResponse>(
                Error.Validation("This vacancy requires approval before it can be published."));

        var previousStatus = vacancy.Status;
        var now = clock.UtcNowOffset();
        var openedAt = request.OpenedAt ?? DateOnly.FromDateTime(now.UtcDateTime);

        vacancy.Open(now, openedAt);
        await db.SaveChangesAsync(cancellationToken);

        // Cross-module read purely for a readable audit Summary line — see VacancyClosedAuditEvent's
        // remarks and the identical pattern in CloseVacancyHandler/UpdateVacancyHandler.
        var effectiveTitle = vacancy.AdvertTitle
            ?? (await positionProfileReader.GetSummaryAsync(request.CompanyId, vacancy.PositionProfileId, cancellationToken))?.Title
            ?? "(untitled)";

        await auditPublisher.PublishAsync(
            new VacancyPublishedAuditEvent(vacancy.CompanyId, vacancy.Id, effectiveTitle, previousStatus, openedAt, now),
            cancellationToken);

        return Result.Success(new PublishVacancyResponse(
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
