using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed class CreateApplicationHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    RecruitmentStageSeeder stageSeeder)
{
    public async Task<Result<CreateApplicationResponse>> HandleAsync(
        CreateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var vacancyExists = await db.Vacancies
            .AnyAsync(v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId, cancellationToken);

        if (!vacancyExists)
            return Result.Failure<CreateApplicationResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var candidate = await db.Candidates
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId, cancellationToken);

        if (candidate is null)
            return Result.Failure<CreateApplicationResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        // Server-side enforcement (not just UI hiding): an inactive candidate must not be able to
        // pick up new recruitment activity, per the candidate deactivation ticket.
        if (!candidate.IsActive)
            return Result.Failure<CreateApplicationResponse>(
                Error.Validation("This candidate is inactive and cannot be added to a new application."));

        var alreadyApplied = await db.Applications
            .AnyAsync(a => a.VacancyId == request.VacancyId && a.CandidateId == request.CandidateId, cancellationToken);

        if (alreadyApplied)
            return Result.Failure<CreateApplicationResponse>(
                Error.Conflict("This candidate has already applied to this vacancy."));

        // Ticket #78: recruiter must exist in the same company. Deliberately does NOT require the
        // recruiter to be IsActive, or to currently be assigned to this vacancy — the ticket only
        // requires a soft UI warning in those cases ("warns before selecting", not "prevents"), and a
        // historical source attribution should remain settable even against a now-inactive recruiter.
        if (request.Source == ApplicationSource.ExternalRecruiter)
        {
            var recruiterExists = await db.ExternalRecruiters
                .AnyAsync(r => r.Id == request.SourceExternalRecruiterId && r.CompanyId == request.CompanyId, cancellationToken);

            if (!recruiterExists)
                return Result.Failure<CreateApplicationResponse>(
                    Error.NotFound($"External recruiter '{request.SourceExternalRecruiterId}' was not found."));
        }

        // Internal recruitment Ticket 1: the submitted CV must be a Kind = Cv document belonging to
        // this candidate in this company. Looked up tenant-scoped, so another company's document is
        // indistinguishable from a non-existent one. The composite FK enforces ownership again in
        // the database.
        CandidateDocument? submittedCv = null;
        if (request.CvDocumentId is Guid cvDocumentId)
        {
            submittedCv = await db.CandidateDocuments
                .AsNoTracking()
                .SingleOrDefaultAsync(cd => cd.Id == cvDocumentId && cd.CompanyId == request.CompanyId, cancellationToken);

            var violation = submittedCv is null
                ? Application.CvDocumentNotFoundMessage
                : Application.DescribeCvDocumentViolation(submittedCv, request.CompanyId, request.CandidateId);

            if (violation is not null)
                return Result.Failure<CreateApplicationResponse>(Error.Validation(violation));
        }

        var now = clock.UtcNowOffset();

        await stageSeeder.EnsureDefaultStagesSeededAsync(request.CompanyId, now, cancellationToken);

        var initialStageId = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (initialStageId == Guid.Empty)
            return Result.Failure<CreateApplicationResponse>(
                Error.Validation("This company has no active, non-terminal recruitment stage to place a new application on."));

        var application = Application.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.VacancyId,
            request.CandidateId,
            initialStageId,
            request.Notes,
            now,
            request.Source,
            request.SourceExternalRecruiterId);

        if (submittedCv is not null)
            application.AttachCv(submittedCv, now);

        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

        if (application.CvDocumentId is Guid recordedCvId)
        {
            await auditPublisher.PublishAsync(
                new ApplicationCvReferenceChangedAuditEvent(
                    application.CompanyId,
                    application.Id,
                    application.VacancyId,
                    application.CandidateId,
                    PreviousCvDocumentId: null,
                    NewCvDocumentId: recordedCvId,
                    request.PerformedByUserId,
                    now),
                cancellationToken);
        }

        if (application.Source is not null)
        {
            await auditPublisher.PublishAsync(
                new ApplicationSourceSetAuditEvent(
                    application.CompanyId,
                    application.Id,
                    application.VacancyId,
                    application.CandidateId,
                    application.Source.Value,
                    application.SourceExternalRecruiterId,
                    now),
                cancellationToken);
        }

        return Result.Success(new CreateApplicationResponse(
            application.Id,
            application.CompanyId,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt,
            application.Source,
            application.SourceExternalRecruiterId,
            application.CvDocumentId));
    }
}
