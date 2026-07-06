using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed class RejectCandidateHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<RejectCandidateResponse>> HandleAsync(
        RejectCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<RejectCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.Status is ApplicationStatus.Hired or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation($"Cannot reject an application with status '{application.Status}'."));

        var now = clock.UtcNowOffset();

        application.Reject(now, request.RejectionReason);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new RejectCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.Status,
            application.InterviewOutcome,
            application.Notes,
            application.RejectionReason,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
