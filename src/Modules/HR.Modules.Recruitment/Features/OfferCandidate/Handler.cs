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
    RecruitmentStageChangeRecorder recorder)
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

        // Ticket #99 judgement call: since stages are now data-driven, the equivalent of the old
        // "must be Interviewed" rule is "must not already be on a terminal stage" — a company may
        // have zero, one, or several interview-shaped stages ahead of Offer.
        if (currentStage.IsTerminal)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation($"Cannot make an offer for an application already on the terminal stage '{currentStage.Name}'."));

        var offerStage = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive)
            .OrderByDescending(s => s.DisplayOrder)
            .FirstOrDefaultAsync(s => !s.IsTerminal, cancellationToken);

        if (offerStage is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation("This company has no active non-terminal recruitment stage to move this application to."));

        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var now = clock.UtcNowOffset();
        var previousStageId = application.CurrentStageId;

        application.MoveToStage(offerStage.Id, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now);
        await db.SaveChangesAsync(cancellationToken);
        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        // Cross-module read: informational-only employment defaults from the linked Position Profile
        // (owned by HR.Modules.Employees), resolved via the narrow IPositionProfileReader contract. See
        // OfferCandidateResponse's remarks — this does not affect the offer itself.
        var employmentDefaults = await positionProfileReader.GetEmploymentDefaultsAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

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
            employmentDefaults?.LocationName));
    }
}
