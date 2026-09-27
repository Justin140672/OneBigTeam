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
                a.OfferedSalary,
                a.OfferedSalaryFrequency,
                a.OfferedStartDate,
                a.OfferDate,
                a.OfferNotes,
                a.OfferResponseStatus,
                a.OfferMadeAt,
                a.OfferRespondedAt,
                a.CvDocumentId,
                a.Version,
                // Internal recruitment Ticket 6: Application.Source is the ONLY authoritative internal
                // indicator. Candidate.EmployeeId alone is not — an external candidate is linked to an
                // employee when hired — so the employee link is only surfaced for Internal applications.
                IsInternal = a.Source == Domain.ApplicationSource.Internal,
                InternalEmployeeId = a.Source == Domain.ApplicationSource.Internal ? c.EmployeeId : null,
                a.AppointmentStatus,
                a.AppointmentEffectiveDate,
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

        // Internal recruitment Ticket 1: the CV submitted with this application is the exact document
        // the application references — never "the candidate's latest CV", so uploading a newer CV
        // cannot change what this application shows as submitted.
        var cv = row.CvDocumentId is Guid submittedCvId
            ? await db.CandidateDocuments
                .AsNoTracking()
                .Where(cd => cd.Id == submittedCvId && cd.CompanyId == request.CompanyId)
                .Select(cd => new { cd.Id, cd.FileName, cd.ContentType, cd.FileSize, cd.CreatedAt, cd.ScanStatus })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        // The candidate's current (most recently uploaded) CV, returned separately so the UI can offer
        // it as clearly labelled current/legacy material — e.g. for historic applications with no
        // captured CV — without ever presenting it as the submitted CV.
        var currentCv = await db.CandidateDocuments
            .AsNoTracking()
            .Where(cd => cd.CompanyId == request.CompanyId &&
                         cd.CandidateId == row.CandidateId &&
                         cd.Kind == Domain.CandidateDocumentKind.Cv)
            .OrderByDescending(cd => cd.CreatedAt)
            .ThenByDescending(cd => cd.Id) // Same deterministic order as ListCandidateDocuments' IsCurrentCv.
            .Select(cd => new { cd.Id, cd.FileName, cd.ContentType, cd.FileSize, cd.CreatedAt, cd.ScanStatus })
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
            stageHistory,
            row.OfferedSalary,
            row.OfferedSalaryFrequency?.ToString(),
            row.OfferedStartDate,
            row.OfferDate,
            row.OfferNotes,
            row.OfferResponseStatus?.ToString(),
            row.OfferMadeAt,
            row.OfferRespondedAt,
            row.Version,
            currentCv?.Id,
            currentCv?.FileName,
            currentCv?.ContentType,
            currentCv?.FileSize,
            currentCv?.CreatedAt,
            cv?.ScanStatus.ToString(),
            currentCv?.ScanStatus.ToString(),
            row.IsInternal,
            row.InternalEmployeeId,
            row.AppointmentStatus?.ToString(),
            row.AppointmentEffectiveDate));
    }
}
