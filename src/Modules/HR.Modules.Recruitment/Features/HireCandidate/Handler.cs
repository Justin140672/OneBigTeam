using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed class HireCandidateHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<HireCandidateResponse>> HandleAsync(
        HireCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.Status != ApplicationStatus.Offered)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation($"Cannot hire an application with status '{application.Status}'."));

        var now = clock.UtcNowOffset();

        application.Hire(now);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new HireCandidateResponse(
            application.Id,
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
