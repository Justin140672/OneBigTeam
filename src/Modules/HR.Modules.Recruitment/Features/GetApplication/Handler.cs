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
            join s in db.RecruitmentStages.AsNoTracking() on a.CurrentStageId equals s.Id
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
                a.CurrentStageId,
                CurrentStageName = s.Name,
                a.InterviewOutcome,
                a.Notes,
                a.CvReviewNotes,
                a.CvReviewedAt,
                a.CvReviewedByUserId,
                a.WithdrawnAt,
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

        var cv = await db.CandidateDocuments
            .AsNoTracking()
            .Where(cd => cd.CompanyId == request.CompanyId &&
                         cd.CandidateId == row.CandidateId &&
                         cd.Kind == Domain.CandidateDocumentKind.Cv)
            .OrderByDescending(cd => cd.CreatedAt)
            .Select(cd => new { cd.Id, cd.FileName, cd.ContentType, cd.FileSize, cd.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var stageHistory = await db.ApplicationStageHistoryEntries
            .AsNoTracking()
            .Where(e => e.ApplicationId == request.ApplicationId && e.CompanyId == request.CompanyId)
            .OrderBy(e => e.ChangedAt)
            .Select(e => new ApplicationStageHistoryItem(
                e.Id,
                e.PreviousStageId,
                e.NewStageId,
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
            row.CurrentStageId,
            row.CurrentStageName,
            row.InterviewOutcome,
            row.Notes,
            row.WithdrawnAt,
            row.AppliedAt,
            row.CreatedAt,
            row.UpdatedAt,
            row.Source,
            row.SourceExternalRecruiterId,
            sourceRecruiterAgencyName,
            row.CvReviewNotes,
            row.CvReviewedAt,
            row.CvReviewedByUserId,
            cv?.Id,
            cv?.FileName,
            cv?.ContentType,
            cv?.FileSize,
            cv?.CreatedAt,
            stageHistory));
    }
}
