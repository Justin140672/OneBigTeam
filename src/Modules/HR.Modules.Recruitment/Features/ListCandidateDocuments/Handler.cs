using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed class ListCandidateDocumentsHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListCandidateDocumentsResponse>> HandleAsync(
        ListCandidateDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await db.CandidateDocuments
            .AsNoTracking()
            .Where(cd => cd.CompanyId == request.CompanyId && cd.CandidateId == request.CandidateId)
            .OrderByDescending(cd => cd.CreatedAt)
            .ThenByDescending(cd => cd.Id)
            .Select(cd => new
            {
                cd.Id,
                cd.Title,
                cd.Kind,
                cd.FileName,
                cd.FileSize,
                cd.ContentType,
                cd.CreatedAt,
                cd.ScanStatus,
                // Internal recruitment Ticket 2: how many applications record this document as the CV
                // submitted with them — lets the UI show that an older CV is still in use (and is
                // retained) after a replacement has been uploaded.
                ReferencingApplicationCount = db.Applications.Count(a =>
                    a.CompanyId == request.CompanyId && a.CvDocumentId == cd.Id),
            })
            .ToListAsync(cancellationToken);

        // Internal recruitment Ticket 2: the candidate's current CV is the most recently uploaded
        // Kind = Cv document (same rule as GetApplication's CurrentCandidateCv*). Uploading a
        // replacement never overwrites an earlier document; it simply becomes the newest.
        var currentCvId = rows.FirstOrDefault(r => r.Kind == CandidateDocumentKind.Cv)?.Id;

        var items = rows
            .Select(r => new CandidateDocumentListItem(
                r.Id,
                r.Title,
                r.Kind.ToString(),
                r.FileName,
                r.FileSize,
                r.ContentType,
                r.CreatedAt,
                IsCurrentCv: r.Id == currentCvId,
                r.ReferencingApplicationCount,
                r.ScanStatus.ToString(),
                IsDownloadable: r.ScanStatus == CandidateDocumentScanStatus.Clean))
            .ToList();

        return Result.Success(new ListCandidateDocumentsResponse(items));
    }
}
