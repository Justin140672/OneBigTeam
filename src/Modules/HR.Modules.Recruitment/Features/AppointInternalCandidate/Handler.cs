using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Features.AppointInternalCandidate;

/// <summary>
/// Internal recruitment Ticket 7: completes a successful internal application (Source == Internal)
/// by changing the EXISTING employee's role through <see cref="IEmployeeInternalAppointmentService"/>.
/// Never calls <see cref="IEmployeeProvisioningService"/>, never creates an Employee and never
/// publishes CandidateHired, so no new-hire provisioning, onboarding or invitation can be triggered.
///
/// Eligibility (mirrors HireCandidate wherever the two overlap):
///  - the application is Internal, belongs to this vacancy and company, and is not already appointed;
///  - it is not withdrawn and is not on a terminal stage (so not rejected or already hired);
///  - an offer that was declined or withdrawn blocks it; an accepted offer is NOT required, exactly as
///    for an external hire (an offer awaiting a response, or none recorded, may still proceed);
///  - the candidate is linked to an employee of the same company whose employment is Active;
///  - the company has an active Hired terminal stage;
///  - the vacancy's position profile resolves with a department and a location.
///
/// Two modules commit separately, so the workflow is ordered to be recoverable:
///  1. Recruitment saves the application as appointment Pending (optimistic concurrency, so two
///     concurrent requests cannot both proceed from the same version).
///  2. Employees records the change keyed by "recruitment:application:{id}" — idempotent, so a retry
///     after a partial failure returns the change already recorded instead of recording another.
///  3. Recruitment moves the application to Hired, marks it Completed and publishes events.
/// A failure refused by Employees (nothing committed there) releases the Pending marker. Anything
/// interrupted after step 1 is completed by a retry of this command or by
/// InternalAppointmentReconciliationJob.
/// </summary>
internal sealed class AppointInternalCandidateHandler(
    RecruitmentDbContext db,
    IEmployeeApplicantReader applicantReader,
    IPositionProfileReader positionProfileReader,
    IEmployeeInternalAppointmentService appointmentService,
    InternalAppointmentCompleter completer,
    IClock clock,
    ILogger<AppointInternalCandidateHandler> logger)
{
    public async Task<Result<AppointInternalCandidateResponse>> HandleAsync(
        AppointInternalCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId, cancellationToken);

        if (vacancy is null)
            return Fail(Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Fail(Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.Source != ApplicationSource.Internal)
            return Fail(Error.Validation(
                "Only an internal application can be completed by internal appointment. Use Hire for an external candidate."));

        if (application.AppointmentStatus == InternalAppointmentStatus.Completed)
            return Fail(Error.Conflict("This internal appointment has already been completed."));

        // Recovery: an earlier attempt was interrupted after the Employees module recorded the change.
        // The decision was already made and applied, so complete it as recorded (the new request's
        // values are not re-applied) rather than re-validating against the employee's changed state.
        if (application.AppointmentStatus == InternalAppointmentStatus.Pending)
        {
            var recorded = await appointmentService.ResumeBySourceReferenceAsync(
                request.CompanyId, application.InternalAppointmentSourceReference, performedBy, cancellationToken);

            if (recorded is not null)
            {
                var recoveryHiredStageId = await completer.FindHiredStageIdAsync(request.CompanyId, cancellationToken);
                if (recoveryHiredStageId is null)
                    return Fail(Error.Validation("This company has no active 'Hired' terminal recruitment stage configured."));

                logger.LogInformation(
                    "Resuming interrupted internal appointment for application {ApplicationId} in company {CompanyId} (promotion {PromotionId}).",
                    application.Id, application.CompanyId, recorded.PromotionId);

                return await CompleteAsync(application, recoveryHiredStageId.Value, recorded, performedBy, cancellationToken);
            }
        }

        if (application.WithdrawnAt is not null)
            return Fail(Error.Validation("Cannot appoint from an application that has been withdrawn."));

        if (application.OfferResponseStatus is OfferResponseStatus.Declined or OfferResponseStatus.Withdrawn)
            return Fail(Error.Validation(
                $"Cannot appoint this candidate — the offer was {application.OfferResponseStatus.Value.ToString().ToLowerInvariant()}."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is null)
            return Fail(Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        if (currentStage.IsTerminal)
            return Fail(Error.Validation(
                $"Cannot appoint from an application already on the terminal stage '{currentStage.Name}'."));

        var hiredStageId = await completer.FindHiredStageIdAsync(request.CompanyId, cancellationToken);
        if (hiredStageId is null)
            return Fail(Error.Validation("This company has no active 'Hired' terminal recruitment stage configured."));

        var candidate = await db.Candidates
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId, cancellationToken);

        if (candidate is null)
            return Fail(Error.NotFound($"Candidate '{application.CandidateId}' was not found."));

        if (candidate.EmployeeId is not { } employeeId)
            return Fail(Error.Validation("This internal application's candidate is not linked to an employee."));

        // Company-scoped read: an employee of another company is indistinguishable from a missing one.
        var employee = await applicantReader.GetApplicantAsync(request.CompanyId, employeeId, cancellationToken);
        if (employee is null)
            return Fail(Error.Validation("The employee linked to this application was not found in this company."));

        if (employee.EmploymentState != EmployeeApplicantEmploymentState.Active)
            return Fail(Error.Validation("Only an active employee can be appointed to a new role."));

        if (!request.NoManager && request.ManagerId == employeeId)
            return Fail(Error.Validation("An employee cannot be their own manager."));

        // Same derivation as HireCandidate: the role, department and location all come from the
        // vacancy's own position profile, never from client input.
        var positionProfile = await positionProfileReader.GetSummaryAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        if (positionProfile is null)
            return Fail(Error.NotFound($"Position profile '{vacancy.PositionProfileId}' was not found."));

        if (positionProfile.DepartmentId is null)
            return Fail(Error.Validation("The vacancy's position profile has no department set; cannot appoint without a department."));

        if (positionProfile.LocationId is null)
            return Fail(Error.Validation("The vacancy's position profile has no location set; cannot appoint without a location."));

        var effectiveDate = request.EffectiveDate ?? application.OfferedStartDate;
        if (effectiveDate is null)
            return Fail(Error.Validation(
                "An effective date is required — none was supplied and no proposed start date was recorded on the offer."));

        // Step 1: persist Pending before touching the Employees module.
        var pendingVersion = application.Version;
        application.BeginInternalAppointment(employeeId, performedBy, clock.UtcNowOffset());

        var pendingSave = await db.SaveChangesWithConcurrencyAsync(
            application, pendingVersion, InternalAppointmentCompleter.ConflictMessage, cancellationToken);

        if (pendingSave.IsFailure)
            return Fail(pendingSave.Error);

        // Step 2: record the employee change (idempotent on the application's source reference).
        var appointmentResult = await appointmentService.AppointAsync(
            new InternalAppointmentRequest(
                request.CompanyId,
                employeeId,
                vacancy.PositionProfileId,
                effectiveDate.Value,
                request.NoManager ? null : request.ManagerId,
                application.InternalAppointmentSourceReference,
                BuildReason(vacancy.AdvertTitle ?? positionProfile.Title),
                performedBy,
                request.ConfirmBackdatedEffectiveDate,
                request.CreateCompensationChange
                    ? new InternalAppointmentCompensation(
                        request.CompensationSalaryType!,
                        request.CompensationSalary!.Value,
                        request.CompensationCurrency!,
                        request.CompensationHoursPerWeek,
                        request.CompensationFte,
                        request.CompensationNotes)
                    : null),
            cancellationToken);

        if (appointmentResult.IsFailure)
        {
            // Employees committed nothing, so release the Pending marker; the application stays on its
            // current stage and can be corrected and retried. If this save loses a race, the marker is
            // cleared by InternalAppointmentReconciliationJob instead.
            var abandonVersion = application.Version;
            application.AbandonInternalAppointment(clock.UtcNowOffset());

            var abandonSave = await db.SaveChangesWithConcurrencyAsync(
                application, abandonVersion, InternalAppointmentCompleter.ConflictMessage, cancellationToken);

            if (abandonSave.IsFailure)
                logger.LogWarning(
                    "Could not release the pending internal appointment on application {ApplicationId} in company {CompanyId}; the reconciliation job will clear it.",
                    application.Id, application.CompanyId);

            return Fail(appointmentResult.Error);
        }

        // Step 3: complete the Recruitment side.
        return await CompleteAsync(application, hiredStageId.Value, appointmentResult.Value!, performedBy, cancellationToken);
    }

    private async Task<Result<AppointInternalCandidateResponse>> CompleteAsync(
        Application application,
        Guid hiredStageId,
        InternalAppointmentResult appointment,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var completion = await completer.CompleteAsync(application, hiredStageId, appointment, performedBy, cancellationToken);
        if (completion.IsFailure)
        {
            logger.LogWarning(
                "Internal appointment for application {ApplicationId} in company {CompanyId} recorded employee change {PromotionId} but the application could not be completed ({ErrorCode}); a retry or the reconciliation job will complete it.",
                application.Id, application.CompanyId, appointment.PromotionId, completion.Error.Code);
            return Fail(completion.Error);
        }

        logger.LogInformation(
            "Internal appointment completed: application {ApplicationId}, employee {EmployeeId}, promotion {PromotionId}, company {CompanyId}, effective {EffectiveDate} (applied: {IsApplied}, already recorded: {WasAlreadyRecorded}).",
            application.Id, appointment.EmployeeId, appointment.PromotionId, application.CompanyId,
            appointment.EffectiveDate, appointment.IsApplied, appointment.WasAlreadyRecorded);

        return Result.Success(new AppointInternalCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            appointment.EmployeeId,
            appointment.PromotionId,
            application.CurrentStageId,
            appointment.NewPositionProfileId,
            appointment.NewDepartmentId,
            appointment.NewLocationId,
            appointment.NewManagerId,
            appointment.EffectiveDate,
            appointment.IsApplied,
            appointment.CompensationId,
            InternalAppointmentStatus.Completed.ToString()));
    }

    // EmployeePromotion.Reason is limited to 500 characters.
    private static string BuildReason(string roleTitle)
    {
        var reason = $"Internal appointment: {roleTitle}";
        return reason.Length <= 500 ? reason : reason[..500];
    }

    private static Result<AppointInternalCandidateResponse> Fail(Error error) =>
        Result.Failure<AppointInternalCandidateResponse>(error);
}
