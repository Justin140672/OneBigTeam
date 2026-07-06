using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.DownloadCandidateDocument;

internal sealed class DownloadCandidateDocumentHandler(RecruitmentDbContext db, ICandidateDocumentStorageService storage)
{
    public async Task<Result<Uri>> HandleAsync(
        DownloadCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var storageKey = await db.CandidateDocuments
            .AsNoTracking()
            .Where(cd => cd.Id == request.DocumentId &&
                         cd.CompanyId == request.CompanyId &&
                         cd.CandidateId == request.CandidateId)
            .Select(cd => cd.StorageKey)
            .SingleOrDefaultAsync(cancellationToken);

        if (storageKey is null)
            return Result.Failure<Uri>(Error.NotFound($"Candidate document '{request.DocumentId}' was not found."));

        var url = await storage.GetDownloadUrlAsync(storageKey, cancellationToken);

        return Result.Success(url);
    }
}
