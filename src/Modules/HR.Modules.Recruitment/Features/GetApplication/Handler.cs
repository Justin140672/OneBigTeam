using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetApplication;

internal sealed class GetApplicationHandler(RecruitmentDbContext db)
{
    public async Task<Result<GetApplicationResponse>> HandleAsync(
        GetApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var row = await (
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            where a.Id        == request.ApplicationId
               && a.CompanyId == request.CompanyId
               && a.VacancyId == request.VacancyId
            select new
            {
                a.Id,
                a.VacancyId,
                a.CandidateId,
                c.FirstName,
                c.LastName,
                c.Email,
                a.Status,
                a.InterviewOutcome,
                a.Notes,
                a.AppliedAt,
                a.CreatedAt,
                a.UpdatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<GetApplicationResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        return Result.Success(new GetApplicationResponse(
            row.Id,
            row.VacancyId,
            row.CandidateId,
            row.FirstName,
            row.LastName,
            row.Email,
            row.Status,
            row.InterviewOutcome,
            row.Notes,
            row.AppliedAt,
            row.CreatedAt,
            row.UpdatedAt));
    }
}
