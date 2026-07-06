using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.DeleteCandidateDocument;

internal sealed class DeleteCandidateDocumentHandler(RecruitmentDbContext db, ICandidateDocumentStorageService storage)
{
    public async Task<Result> HandleAsync(
        DeleteCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var document = await db.CandidateDocuments
            .SingleOrDefaultAsync(
                cd => cd.Id == request.DocumentId &&
                      cd.CompanyId == request.CompanyId &&
                      cd.CandidateId == request.CandidateId,
                cancellationToken);

        if (document is null)
            return Result.Failure(Error.NotFound($"Candidate document '{request.DocumentId}' was not found."));

        db.CandidateDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        await storage.DeleteAsync(document.StorageKey, cancellationToken);

        return Result.Success();
    }
}
