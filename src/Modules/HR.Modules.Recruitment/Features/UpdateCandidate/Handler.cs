using HR.Modules.Recruitment.Domain;
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
        var newNormalisedEmail = CandidateEmail.Normalise(newEmail);
        var duplicateEmailError = Error.Conflict($"A candidate with email '{newEmail}' already exists in this company.");

        // Uniqueness is on the normalised email (see CandidateEmailUniqueness), so a case-only change
        // to this candidate's own email is never a conflict, and another candidate differing only in
        // case/whitespace always is.
        if (!string.Equals(candidate.NormalisedEmail, newNormalisedEmail, StringComparison.Ordinal))
        {
            var existing = await CandidateEmailUniqueness.FindExistingAsync(
                db, request.CompanyId, newNormalisedEmail, cancellationToken, excludingCandidateId: request.CandidateId);

            if (existing is not null)
                return Result.Failure<UpdateCandidateResponse>(duplicateEmailError);
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

        Result saveResult;
        try
        {
            saveResult = await db.SaveChangesWithConcurrencyAsync(
                candidate,
                request.ExpectedVersion,
                "This candidate was changed by someone else since you opened it. Reload the latest details and try again.",
                cancellationToken);
        }
        catch (DbUpdateException ex) when (CandidateEmailUniqueness.IsViolation(ex))
        {
            // Final safeguard: another writer claimed the new email after the check above.
            return Result.Failure<UpdateCandidateResponse>(duplicateEmailError);
        }

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
