using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed class CreateApplicationHandler(RecruitmentDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
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

        var candidateExists = await db.Candidates
            .AnyAsync(c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId, cancellationToken);

        if (!candidateExists)
            return Result.Failure<CreateApplicationResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

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

        var now = clock.UtcNowOffset();

        var application = Application.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.VacancyId,
            request.CandidateId,
            request.Notes,
            now,
            request.Source,
            request.SourceExternalRecruiterId);

        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

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
            application.Status,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt,
            application.Source,
            application.SourceExternalRecruiterId));
    }
}
