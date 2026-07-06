using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed class OfferCandidateHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<OfferCandidateResponse>> HandleAsync(
        OfferCandidateRequest request,
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

        if (application.Status != ApplicationStatus.Interviewed)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation($"Cannot make an offer for an application with status '{application.Status}'."));

        var now = clock.UtcNowOffset();

        application.Offer(now);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new OfferCandidateResponse(
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
