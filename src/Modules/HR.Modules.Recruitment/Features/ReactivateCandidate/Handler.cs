using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ReactivateCandidate;

internal sealed class ReactivateCandidateHandler(RecruitmentDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ReactivateCandidateResponse>> HandleAsync(
        ReactivateCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates
            .SingleOrDefaultAsync(
                c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId,
                cancellationToken);

        if (candidate is null)
            return Result.Failure<ReactivateCandidateResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        if (candidate.IsActive)
            return Result.Failure<ReactivateCandidateResponse>(
                Error.Conflict("This candidate is already active."));

        var now = clock.UtcNowOffset();

        candidate.Reactivate(performedBy, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new CandidateReactivatedAuditEvent(
                candidate.CompanyId,
                candidate.Id,
                $"{candidate.FirstName} {candidate.LastName}",
                performedBy,
                now),
            cancellationToken);

        return Result.Success(new ReactivateCandidateResponse(
            candidate.Id,
            candidate.CompanyId,
            candidate.IsActive,
            candidate.ReactivatedAt,
            candidate.ReactivatedByUserId,
            candidate.UpdatedAt));
    }
}
