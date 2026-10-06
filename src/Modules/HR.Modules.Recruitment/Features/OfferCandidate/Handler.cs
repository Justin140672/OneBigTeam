using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed class OfferCandidateHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    RecruitmentStageChangeRecorder recorder,
    ICompanyRecruitmentSettingsReader recruitmentSettingsReader,
    IAuditEventPublisher auditPublisher,
    IEmployeeApplicantReader applicantReader,
    OfferTermsSnapshotFactory snapshotFactory,
    InternalOfferTaskEffectsService effectsService)
{
    public async Task<Result<OfferCandidateResponse>> HandleAsync(
        OfferCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, OfferCandidateResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    await effectsService.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<OfferCandidateResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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
        var candidateInfo = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId)
            .Select(c => new { c.IsActive, c.EmployeeId })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidateInfo is not { IsActive: true })
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation("Cannot make an offer to an inactive candidate."));

        if (application.AppointmentStatus is not null)
            return Result.Failure<OfferCandidateResponse>(
                Error.Conflict("Cannot revise the offer once the internal appointment has started."));

        var isInternal = application.Source == ApplicationSource.Internal;
        Guid internalEmployeeId = Guid.Empty;

        if (isInternal)
        {
            var internalValidation = await ValidateInternalOfferAsync(request, candidateInfo.EmployeeId, cancellationToken);
            if (internalValidation.Error is not null)
                return Result.Failure<OfferCandidateResponse>(internalValidation.Error);

            internalEmployeeId = internalValidation.EmployeeId;
        }

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        if (currentStage.IsTerminal)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation($"Cannot make an offer for an application already on the terminal stage '{currentStage.Name}'."));

        var activeNonTerminalStages = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive && !s.IsTerminal)
            .OrderByDescending(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var interviews = await db.Interviews
            .AsNoTracking()
            .Where(i => i.ApplicationId == application.Id && i.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var violation = InterviewStageWorkflow.DescribeOfferViolation(activeNonTerminalStages, interviews);

        if (violation is not null)
            return Result.Failure<OfferCandidateResponse>(Error.Validation(violation));

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

        var employmentDefaults = await positionProfileReader.GetEmploymentDefaultsAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var previousStageId = application.CurrentStageId;
        var expectedVersion = application.Version;

        var offeredSalary = request.OfferedSalary ?? employmentDefaults?.SalaryMin;

        OfferSalaryFrequency? offeredFrequency =
            !string.IsNullOrWhiteSpace(request.OfferedSalaryFrequency) &&
            Enum.TryParse<OfferSalaryFrequency>(request.OfferedSalaryFrequency, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : Enum.TryParse<OfferSalaryFrequency>(employmentDefaults?.SalaryType, ignoreCase: true, out var fromProfile) && Enum.IsDefined(fromProfile)
                    ? fromProfile
                    : null;

        var offerDate = request.OfferDate ?? today;

        OfferTermsSnapshot? snapshot = null;

        if (isInternal)
        {
            if (offeredSalary is null)
                return Result.Failure<OfferCandidateResponse>(
                    Error.Validation("An offered salary is required for an internal offer."));

            snapshot = await snapshotFactory.BuildAsync(
                vacancy,
                new OfferTermsInput(
                    request.ProposedManagerId, request.NoManager, request.Currency,
                    request.HoursPerWeek, request.Fte, request.ResponseDeadline),
                cancellationToken);
        }

        application.MoveToStage(offerStage.Id, now);
        application.RecordOfferTerms(
            offeredSalary, offeredFrequency, request.ProposedStartDate, offerDate, request.OfferNotes, now,
            snapshot, performedBy);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now);

        if (isInternal)
        {
            db.InternalOfferTaskEffects.Add(InternalOfferTaskEffect.Create(
                Guid.NewGuid(),
                application.CompanyId,
                application.Id,
                application.OfferVersion,
                internalEmployeeId,
                performedBy,
                snapshot!.JobTitle ?? "your new role",
                request.ResponseDeadline,
                now));
        }

        var response = new OfferCandidateResponse(
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
            application.OfferRespondedAt,
            isInternal ? OfferTermsView.From(application) : null);

        // Ticket 14 (P2): SaveIdempotentWithConcurrencyAsync pins/advances application's version and
        // translates a stale-save DbUpdateConcurrencyException the same way the non-idempotent
        // branch below does — an Idempotency-Key must not bypass optimistic-concurrency protection.
        const string conflictMessage = "This application was changed by someone else. Reload and try again.";

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, Application, OfferCandidateResponse>(
                db.IdempotencyRecords, application, expectedVersion, scope, key, fingerprint!,
                StatusCodes.Status200OK, response, now, cancellationToken);

            switch (outcome.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    await effectsService.RunOutstandingForApplicationAsync(request.CompanyId, request.ApplicationId, cancellationToken);
                    return Result.Success(outcome.Response!);
                case IdempotencyOutcomeKind.ConcurrencyConflict:
                    return Result.Failure<OfferCandidateResponse>(Error.Concurrency(conflictMessage));
            }
        }
        else
        {
            // Ticket 6 (P1): see MoveApplicationStageHandler's matching guard.
            var saveResult = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, conflictMessage, cancellationToken);

            if (!saveResult.IsSuccess)
                return Result.Failure<OfferCandidateResponse>(saveResult.Error);
        }

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
                now,
                application.OfferVersion),
            cancellationToken);

        if (isInternal)
            await effectsService.RunOutstandingForApplicationAsync(application.CompanyId, application.Id, cancellationToken);

        return Result.Success(response);
    }

    private async Task<(Guid EmployeeId, Error? Error)> ValidateInternalOfferAsync(
        OfferCandidateRequest request,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        if (employeeId is not { } linkedEmployeeId)
            return (Guid.Empty, Error.Validation("This internal application's candidate is not linked to an employee."));

        var employee = await applicantReader.GetApplicantAsync(request.CompanyId, linkedEmployeeId, cancellationToken);
        if (employee is null)
            return (Guid.Empty, Error.Validation("The employee linked to this application was not found in this company."));

        if (employee.EmploymentState != EmployeeApplicantEmploymentState.Active)
            return (Guid.Empty, Error.Validation("An internal offer can only be made to an active employee."));

        if (request.ProposedStartDate is null)
            return (Guid.Empty, Error.Validation("A proposed effective date is required for an internal offer."));

        if (string.IsNullOrWhiteSpace(request.Currency))
            return (Guid.Empty, Error.Validation("A currency is required for an internal offer."));

        if (!request.NoManager && request.ProposedManagerId is null)
            return (Guid.Empty, Error.Validation("Select a proposed manager, or choose 'No manager'."));

        if (!request.NoManager && request.ProposedManagerId == linkedEmployeeId)
            return (Guid.Empty, Error.Validation("An employee cannot be their own manager."));

        if (request.ResponseDeadline is { } deadline && deadline < DateOnly.FromDateTime(clock.UtcNowOffset().UtcDateTime))
            return (Guid.Empty, Error.Validation("The response deadline cannot be in the past."));

        if (!request.NoManager && request.ProposedManagerId is { } managerId)
        {
            var manager = await applicantReader.GetApplicantAsync(request.CompanyId, managerId, cancellationToken);
            if (manager is null)
                return (Guid.Empty, Error.Validation("The proposed manager was not found in this company."));
        }

        return (linkedEmployeeId, null);
    }
}
