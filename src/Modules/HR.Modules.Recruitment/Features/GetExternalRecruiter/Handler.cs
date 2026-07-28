using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiter;

internal sealed class GetExternalRecruiterHandler(RecruitmentDbContext db)
{
    public async Task<Result<GetExternalRecruiterResponse>> HandleAsync(
        GetExternalRecruiterRequest request,
        CancellationToken cancellationToken)
    {
        var recruiter = await db.ExternalRecruiters
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == request.ExternalRecruiterId && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (recruiter is null)
            return Result.Failure<GetExternalRecruiterResponse>(
                Error.NotFound($"External recruiter '{request.ExternalRecruiterId}' was not found."));

        return Result.Success(new GetExternalRecruiterResponse(
            recruiter.Id,
            recruiter.CompanyId,
            recruiter.AgencyName,
            recruiter.ContactName,
            recruiter.ContactEmail,
            recruiter.ContactTelephone,
            recruiter.Website,
            recruiter.Notes,
            recruiter.IsActive,
            recruiter.CreatedAt,
            recruiter.UpdatedAt));
    }
}
