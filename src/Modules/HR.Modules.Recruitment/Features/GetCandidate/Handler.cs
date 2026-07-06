using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetCandidate;

internal sealed class GetCandidateHandler(RecruitmentDbContext db)
{
    public async Task<Result<GetCandidateResponse>> HandleAsync(
        GetCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId,
                cancellationToken);

        if (candidate is null)
            return Result.Failure<GetCandidateResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        return Result.Success(new GetCandidateResponse(
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
