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
                a.Source,
                a.SourceExternalRecruiterId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<GetApplicationResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        string? sourceRecruiterAgencyName = null;
        if (row.SourceExternalRecruiterId is not null)
        {
            sourceRecruiterAgencyName = await db.ExternalRecruiters
                .AsNoTracking()
                .Where(r => r.Id == row.SourceExternalRecruiterId && r.CompanyId == request.CompanyId)
                .Select(r => r.AgencyName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var stageHistory = await db.ApplicationStageHistoryEntries
            .AsNoTracking()
            .Where(e => e.ApplicationId == request.ApplicationId && e.CompanyId == request.CompanyId)
            .OrderBy(e => e.ChangedAt)
            .Select(e => new ApplicationStageHistoryItem(
                e.Id,
                e.PreviousStage,
                e.NewStage,
                e.ChangedByUserId,
                e.Notes,
                e.ChangedAt))
            .ToListAsync(cancellationToken);

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
            row.UpdatedAt,
            row.Source,
            row.SourceExternalRecruiterId,
            sourceRecruiterAgencyName,
            stageHistory));
    }
}
