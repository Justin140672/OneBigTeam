using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateVacancy;

internal sealed class UpdateVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<UpdateVacancyResponse>> HandleAsync(
        UpdateVacancyRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<UpdateVacancyResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var now = clock.UtcNowOffset();

        Guid? previousPositionProfileId = null;
        Guid? newPositionProfileId = null;
        var isAuthorisedCorrection = false;

        if (request.PositionProfileId is { } requestedPositionProfileId
            && requestedPositionProfileId != vacancy.PositionProfileId)
        {
            // Baseline change-control check: a Position Profile may only be swapped post-creation
            // while the vacancy is still Draft and has received zero applications. See
            // CanChangePositionProfile's remarks.
            var applicationCount = await db.Applications
                .CountAsync(a => a.VacancyId == vacancy.Id, cancellationToken);

            if (!CanChangePositionProfile(vacancy.Status, applicationCount))
            {
                // Authorised correction escape hatch: the endpoint already restricts this action to
                // "recruitment:manage" (Recruiter role) — the same role required for the normal
                // Draft-only path. This codebase's only precedent for a stricter tier within an
                // already-narrow single-role domain policy is composite policies like
                // leave:approve/leave:manage, which separate genuinely distinct capabilities (approve
                // vs. full management) across different roles, not a "normal vs. override" split
                // within one capability. Recruitment has no such split, and recruitment:manage is
                // already scoped to a single role, so a new permission tier isn't justified here —
                // the reason requirement (enforced by the validator) plus the distinct audit trail
                // below are the guardrail instead.
                if (!request.IsAuthorisedCorrection || string.IsNullOrWhiteSpace(request.CorrectionReason))
                    return Result.Failure<UpdateVacancyResponse>(
                        Error.Validation(
                            "Position Profile cannot be changed once the vacancy has been published or has applications, unless an authorised correction is supplied."));

                isAuthorisedCorrection = true;
            }

            // Cross-module validation: PositionProfile is owned by HR.Modules.Employees, so existence
            // and company-ownership are verified through the narrow IPositionProfileReader contract
            // rather than a direct module reference or a database foreign key — same pattern as
            // CreateVacancyHandler.
            var positionProfileExists = await positionProfileReader.ExistsAsync(
                request.CompanyId, requestedPositionProfileId, cancellationToken);

            if (!positionProfileExists)
                return Result.Failure<UpdateVacancyResponse>(
                    Error.NotFound($"Position profile '{requestedPositionProfileId}' was not found."));

            previousPositionProfileId = vacancy.PositionProfileId;
            newPositionProfileId = requestedPositionProfileId;

            vacancy.ChangePositionProfile(requestedPositionProfileId, now);
        }

        // Ticket #81: AssignedRecruiterId is now an optional FK to ExternalRecruiter (the external
        // agency), not an Employee — existence/company-ownership/active checks happen here via direct
        // EF Core access, since ExternalRecruiter lives in this same module/schema (unlike
        // PositionProfileId, which requires the cross-module IPositionProfileReader contract).
        if (request.AssignedRecruiterId is { } requestedRecruiterId
            && requestedRecruiterId != vacancy.AssignedRecruiterId)
        {
            var recruiter = await db.ExternalRecruiters
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.Id == requestedRecruiterId && r.CompanyId == request.CompanyId,
                    cancellationToken);

            if (recruiter is null)
                return Result.Failure<UpdateVacancyResponse>(
                    Error.NotFound($"External recruiter '{requestedRecruiterId}' was not found."));

            if (!recruiter.IsActive)
                return Result.Failure<UpdateVacancyResponse>(
                    Error.Validation($"External recruiter '{recruiter.AgencyName}' is inactive and cannot be assigned to a vacancy."));
        }

        var before = new VacancyAuditSnapshot(
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.HiringManagerId,
            vacancy.Status,
            vacancy.AssignedRecruiterId);

        vacancy.UpdateDetails(
            request.AdvertTitle,
            request.AdvertDescription,
            request.HiringManagerId,
            request.AssignedRecruiterId,
            now);

        await db.SaveChangesAsync(cancellationToken);

        var after = new VacancyAuditSnapshot(
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.HiringManagerId,
            vacancy.Status,
            vacancy.AssignedRecruiterId);

        // Cross-module read purely for a readable audit Summary line — see VacancyUpdatedAuditEvent's
        // remarks. Deliberately resolved after the vacancy's own state has already been persisted, so
        // a Position Profile that can no longer be found never blocks the update itself.
        var effectiveTitle = vacancy.AdvertTitle
            ?? (await positionProfileReader.GetSummaryAsync(request.CompanyId, vacancy.PositionProfileId, cancellationToken))?.Title
            ?? "(untitled)";

        await auditPublisher.PublishAsync(
            new VacancyUpdatedAuditEvent(vacancy.CompanyId, vacancy.Id, before, after, effectiveTitle, now),
            cancellationToken);

        if (newPositionProfileId is not null)
        {
            await auditPublisher.PublishAsync(
                new VacancyPositionProfileAssignedAuditEvent(
                    vacancy.CompanyId,
                    vacancy.Id,
                    previousPositionProfileId,
                    newPositionProfileId.Value,
                    isAuthorisedCorrection ? "authorised_correction" : "update",
                    now,
                    isAuthorisedCorrection ? performedBy : null,
                    isAuthorisedCorrection ? request.CorrectionReason : null),
                cancellationToken);
        }

        return Result.Success(new UpdateVacancyResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.PositionProfileId,
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.Status,
            vacancy.HiringManagerId,
            vacancy.AssignedRecruiterId,
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt));
    }

    /// <summary>
    /// Baseline "safely changeable" rule for changing a vacancy's Position Profile after creation:
    /// the vacancy must still be Draft and have received zero applications. Deliberately isolated as
    /// its own method (rather than inlined in HandleAsync) so the "Prevent Invalid Position Profile
    /// Changes" story can extend it with an authorised correction-workflow override without having to
    /// restructure the surrounding handler logic.
    /// </summary>
    internal static bool CanChangePositionProfile(Domain.VacancyStatus status, int applicationCount) =>
        status == Domain.VacancyStatus.Draft && applicationCount == 0;
}
