using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed class CreateApplicationHandler(RecruitmentDbContext db, IClock clock)
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

        var now = clock.UtcNowOffset();

        var application = Application.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.VacancyId,
            request.CandidateId,
            request.Notes,
            now);

        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

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
            application.UpdatedAt));
    }
}
