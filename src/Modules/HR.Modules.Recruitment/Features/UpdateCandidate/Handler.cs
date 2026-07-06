using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateCandidate;

internal sealed class UpdateCandidateHandler(RecruitmentDbContext db, IClock clock)
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

        candidate.UpdateDetails(
            request.FirstName,
            request.LastName,
            newEmail,
            request.Phone,
            request.ResumeUrl,
            now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateCandidateResponse(
            candidate.Id,
            candidate.CompanyId,
            candidate.FirstName,
            candidate.LastName,
            candidate.Email,
            candidate.Phone,
            candidate.ResumeUrl,
            candidate.CreatedAt,
            candidate.UpdatedAt));
    }
}
