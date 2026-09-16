using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateCandidate;

internal sealed class UpdateCandidateHandler(RecruitmentDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<UpdateCandidateResponse>> HandleAsync(
        UpdateCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates
            .SingleOrDefaultAsync(
                c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId,
                cancellationToken);

        if (candidate is null)
            return Result.Failure<UpdateCandidateResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        // Ticket 7 (P2): a purged candidate's personal fields were redacted by an explicit,
        // separately-authorised retention action (PurgeEligibleCandidatesHandler) — an ordinary
        // update must never be able to repopulate them.
        if (candidate.PurgedAt is not null)
            return Result.Failure<UpdateCandidateResponse>(
                Error.Conflict("This candidate's data has been purged under the retention policy and can no longer be edited."));

        var newEmail = request.Email.Trim();
        if (!string.Equals(candidate.Email, newEmail, StringComparison.Ordinal))
        {
            var emailExists = await db.Candidates
                .AnyAsync(
                    c => c.CompanyId == request.CompanyId &&
                         c.Id != request.CandidateId &&
                         c.Email == newEmail,
                    cancellationToken);

            if (emailExists)
            {
                return Result.Failure<UpdateCandidateResponse>(
                    Error.Conflict($"A candidate with email '{newEmail}' already exists in this company."));
            }
        }

        var now = clock.UtcNowOffset();

        var before = new CandidateAuditSnapshot(
            candidate.FirstName,
            candidate.LastName,
            candidate.Email,
            candidate.Phone,
            candidate.ResumeUrl);

        candidate.UpdateDetails(
            request.FirstName,
            request.LastName,
            newEmail,
            request.Phone,
            request.ResumeUrl,
            now);

        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            candidate,
            request.ExpectedVersion,
            "This candidate was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateCandidateResponse>(saveResult.Error);

        var after = new CandidateAuditSnapshot(
            candidate.FirstName,
            candidate.LastName,
            candidate.Email,
            candidate.Phone,
            candidate.ResumeUrl);

        await auditPublisher.PublishAsync(
            new CandidateUpdatedAuditEvent(candidate.CompanyId, candidate.Id, before, after, now),
            cancellationToken);

        return Result.Success(new UpdateCandidateResponse(
            candidate.Id,
            candidate.CompanyId,
            candidate.FirstName,
            candidate.LastName,
            candidate.Email,
            candidate.Phone,
            candidate.ResumeUrl,
            candidate.CreatedAt,
            candidate.UpdatedAt,
            candidate.Version));
    }
}
