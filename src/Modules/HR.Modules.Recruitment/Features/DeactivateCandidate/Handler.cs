using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.DeactivateCandidate;

internal sealed class DeactivateCandidateHandler(RecruitmentDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<DeactivateCandidateResponse>> HandleAsync(
        DeactivateCandidateRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates
            .SingleOrDefaultAsync(
                c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId,
                cancellationToken);

        if (candidate is null)
            return Result.Failure<DeactivateCandidateResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        if (!candidate.IsActive)
            return Result.Failure<DeactivateCandidateResponse>(
                Error.Conflict("This candidate is already inactive."));

        // Product decision (ticket had no fixed definition): an "active application" is one that is
        // neither withdrawn nor sitting on a terminal RecruitmentStage (Hired/Rejected). Blocking
        // deactivation while any such application exists forces the recruiter to resolve (reject,
        // withdraw, hire, etc.) every open application first, rather than silently leaving orphaned
        // in-flight recruitment activity behind a deactivated candidate.
        var hasActiveApplication = await db.Applications
            .Where(a => a.CandidateId == request.CandidateId && a.CompanyId == request.CompanyId && a.WithdrawnAt == null)
            .Join(
                db.RecruitmentStages.Where(s => s.CompanyId == request.CompanyId),
                a => a.CurrentStageId,
                s => s.Id,
                (a, s) => s.IsTerminal)
            .AnyAsync(isTerminal => !isTerminal, cancellationToken);

        if (hasActiveApplication)
            return Result.Failure<DeactivateCandidateResponse>(
                Error.Validation(
                    "This candidate has one or more active applications. Withdraw, reject or otherwise resolve every open application before deactivating the candidate."));

        var now = clock.UtcNowOffset();

        candidate.Deactivate(performedBy, request.Reason, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new CandidateDeactivatedAuditEvent(
                candidate.CompanyId,
                candidate.Id,
                $"{candidate.FirstName} {candidate.LastName}",
                candidate.DeactivationReason!,
                performedBy,
                now),
            cancellationToken);

        return Result.Success(new DeactivateCandidateResponse(
            candidate.Id,
            candidate.CompanyId,
            candidate.IsActive,
            candidate.DeactivatedAt,
            candidate.DeactivatedByUserId,
            candidate.DeactivationReason,
            candidate.UpdatedAt));
    }
}
