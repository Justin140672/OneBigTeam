using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed class OfferCandidateHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    RecruitmentStageChangeRecorder recorder,
    ICompanyRecruitmentSettingsReader recruitmentSettingsReader,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<OfferCandidateResponse>> HandleAsync(
        OfferCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation("Cannot make an offer for an application that has been withdrawn."));

        // Server-side enforcement (not just UI hiding): an inactive candidate must not be able to
        // pick up new recruitment activity, per the candidate deactivation ticket.
        var candidateIsActive = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId)
            .Select(c => c.IsActive)
            .SingleOrDefaultAsync(cancellationToken);

        if (!candidateIsActive)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation("Cannot make an offer to an inactive candidate."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        if (currentStage.IsTerminal)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation($"Cannot make an offer for an application already on the terminal stage '{currentStage.Name}'."));

        // Prefer the stage the company has explicitly flagged as its Offer stage
        // (RecruitmentStagePurpose.Offer); fall back to "last non-terminal stage by DisplayOrder".
        var activeNonTerminalStages = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive && !s.IsTerminal)
            .OrderByDescending(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var offerStage = activeNonTerminalStages
            .FirstOrDefault(s => s.Purpose == RecruitmentStagePurpose.Offer)
            ?? activeNonTerminalStages.FirstOrDefault();

        if (offerStage is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation("This company has no active non-terminal recruitment stage to move this application to."));

        // SET-05: when the company requires offer approval, an offer cannot be made until this
        // specific application has been explicitly approved via the ApproveOffer endpoint. Recording
        // offer terms must NOT bypass this rule — the check stays exactly where it was, ahead of any
        // state change.
        var recruitmentSettings = await recruitmentSettingsReader.GetRecruitmentSettingsAsync(request.CompanyId, cancellationToken);
        if (recruitmentSettings.OfferApprovalRequired && application.OfferApprovedAt is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation("This offer requires approval before it can be made."));

        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        // Cross-module read: informational-only employment defaults from the linked Position Profile
        // (owned by HR.Modules.Employees), resolved via the narrow IPositionProfileReader contract.
        // Read before recording terms so an omitted salary/frequency can be pre-populated from the
        // role's defined compensation — HR can still override with the actual agreed figure.
        var employmentDefaults = await positionProfileReader.GetEmploymentDefaultsAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var previousStageId = application.CurrentStageId;

        var offeredSalary = request.OfferedSalary ?? employmentDefaults?.SalaryMin;

        OfferSalaryFrequency? offeredFrequency =
            !string.IsNullOrWhiteSpace(request.OfferedSalaryFrequency) &&
            Enum.TryParse<OfferSalaryFrequency>(request.OfferedSalaryFrequency, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : Enum.TryParse<OfferSalaryFrequency>(employmentDefaults?.SalaryType, ignoreCase: true, out var fromProfile) && Enum.IsDefined(fromProfile)
                    ? fromProfile
                    : null;

        var offerDate = request.OfferDate ?? today;

        application.MoveToStage(offerStage.Id, now);
        application.RecordOfferTerms(offeredSalary, offeredFrequency, request.ProposedStartDate, offerDate, request.OfferNotes, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now);
        await db.SaveChangesAsync(cancellationToken);
        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        // Salary figures are deliberately excluded from the audit payload (05-database-standards /
        // 09-coding-standards: salary must not appear in audit payloads) — the event records only
        // that an offer was made, its dates and its response status.
        await auditPublisher.PublishAsync(
            new OfferDetailsRecordedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                offerDate,
                request.ProposedStartDate,
                performedBy,
                now),
            cancellationToken);

        return Result.Success(new OfferCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt,
            vacancy.PositionProfileId,
            employmentDefaults?.Title,
            employmentDefaults?.SalaryMin,
            employmentDefaults?.SalaryMax,
            employmentDefaults?.SalaryType,
            employmentDefaults?.WorkingDaysOverride,
            employmentDefaults?.HoursPerDayOverride,
            employmentDefaults?.ProbationMonthsOverride,
            employmentDefaults?.DefaultLeavePolicyId,
            employmentDefaults?.LocationName,
            application.OfferedSalary,
            application.OfferedSalaryFrequency?.ToString(),
            application.OfferedStartDate,
            application.OfferDate,
            application.OfferNotes,
            application.OfferResponseStatus?.ToString(),
            application.OfferMadeAt,
            application.OfferRespondedAt));
    }
}
