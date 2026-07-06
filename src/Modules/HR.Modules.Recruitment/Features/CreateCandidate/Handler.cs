using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed class CreateCandidateHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<CreateCandidateResponse>> HandleAsync(
        CreateCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        var emailExists = await db.Candidates
            .AnyAsync(
                c => c.CompanyId == request.CompanyId && c.Email == email,
                cancellationToken);

        if (emailExists)
        {
            return Result.Failure<CreateCandidateResponse>(
                Error.Conflict($"A candidate with email '{email}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var candidate = Candidate.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.FirstName,
            request.LastName,
            email,
            request.Phone,
            request.ResumeUrl,
            now);

        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateCandidateResponse(
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
