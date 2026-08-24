using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed class HireCandidateHandler(
    RecruitmentDbContext db,
    IEmployeeProvisioningService employeeProvisioningService,
    IPositionProfileReader positionProfileReader,
    IClock clock,
    IIntegrationEventPublisher eventPublisher,
    IAuditEventPublisher auditPublisher,
    RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<HireCandidateResponse>> HandleAsync(
        HireCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation("Cannot hire an application that has been withdrawn."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        // Ticket #99: since the pipeline is now fully data-driven, "eligible to be hired from" is
        // simply "not already on a terminal stage" — the old fixed rule (must be Interviewed) no
        // longer applies once companies can freely insert/reorder stages ahead of Offer.
        if (currentStage.IsTerminal)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation($"Cannot hire an application already on the terminal stage '{currentStage.Name}'."));

        var hiredStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.CompanyId == request.CompanyId && s.IsActive && s.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired,
                cancellationToken);

        if (hiredStage is null)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation("This company has no active 'Hired' terminal recruitment stage configured."));

        var candidate = await db.Candidates
            .SingleOrDefaultAsync(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId, cancellationToken);

        if (candidate is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Candidate '{application.CandidateId}' was not found."));

        if (candidate.EmployeeId is not null)
            return Result.Failure<HireCandidateResponse>(
                Error.Conflict("This candidate is already linked to an employee."));

        // The hired employee is assigned to the Vacancy's own Position Profile — never to an
        // independently-entered value — so Department and Location are both derived here via the
        // narrow IPositionProfileReader contract, the same cross-module read pattern
        // CreateVacancyHandler already uses for Department. Vacancy.PositionProfileId is mandatory
        // (non-nullable), so this always resolves to a real Position Profile; if it can no longer be
        // found (e.g. hard-deleted out from under an existing Vacancy), hiring cannot proceed.
        var positionProfileSummary = await positionProfileReader.GetSummaryAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        if (positionProfileSummary is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Position profile '{vacancy.PositionProfileId}' was not found."));

        if (positionProfileSummary.DepartmentId is null)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation("The vacancy's position profile has no department set; cannot hire without a department."));

        if (positionProfileSummary.LocationId is null)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation("The vacancy's position profile has no location set; cannot hire without a location."));

        var provisioningResult = await employeeProvisioningService.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                request.CompanyId,
                candidate.FirstName,
                candidate.LastName,
                candidate.Email,
                request.StartDate,
                request.DateOfBirth,
                request.Nationality,
                request.Gender,
                request.EmployeeNumber,
                request.EmploymentTypeId,
                positionProfileSummary.DepartmentId.Value,
                positionProfileSummary.LocationId.Value,
                vacancy.PositionProfileId,
                request.GenderOther,
                PersonalEmail: null,
                candidate.Phone,
                request.ManagerId,
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.County,
                request.PostCode),
            cancellationToken);

        if (provisioningResult.IsFailure)
            return Result.Failure<HireCandidateResponse>(provisioningResult.Error);

        var now = clock.UtcNowOffset();
        var previousStageId = application.CurrentStageId;

        application.RecordHire(hiredStage.Id, now);
        candidate.LinkToEmployee(provisioningResult.Value!, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now);
        await db.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(new CandidateHiredIntegrationEvent(
            application.CompanyId,
            application.Id,
            candidate.Id,
            provisioningResult.Value!,
            application.VacancyId,
            now), cancellationToken);

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        await auditPublisher.PublishAsync(
            new CandidateHiredAuditEvent(
                application.CompanyId,
                candidate.Id,
                application.Id,
                application.VacancyId,
                provisioningResult.Value!,
                now),
            cancellationToken);

        return Result.Success(new HireCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            provisioningResult.Value!,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
